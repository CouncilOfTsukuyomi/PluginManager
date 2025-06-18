
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using PluginManager.Core.Interfaces;

namespace PluginManager.Core.Security;

public class IsolatedPluginLoader : IDisposable
{
    private readonly ILogger<IsolatedPluginLoader> _logger;
    private readonly string _pluginDirectory;
    private PluginLoadContext? _loadContext;
    private bool _disposed;

    public IsolatedPluginLoader(ILogger<IsolatedPluginLoader> logger, string pluginDirectory)
    {
        _logger = logger;
        _pluginDirectory = pluginDirectory;
    }

    public async Task<IModPlugin?> LoadPluginAsync(string assemblyPath, string typeName)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(IsolatedPluginLoader));

        try
        {
            _logger.LogDebug("Loading plugin in isolated context: {AssemblyPath} -> {TypeName}", assemblyPath, typeName);

            // Create isolated load context
            _loadContext = new PluginLoadContext(assemblyPath, _logger);
            
            // Load the plugin assembly
            var assembly = _loadContext.LoadFromAssemblyPath(assemblyPath);
            var pluginType = assembly.GetType(typeName);
            
            if (pluginType == null || !typeof(IModPlugin).IsAssignableFrom(pluginType))
            {
                _logger.LogWarning("Type {TypeName} not found or doesn't implement IModPlugin", typeName);
                return null;
            }

            var plugin = await CreatePluginInstanceAsync(pluginType);
            
            if (plugin != null)
            {
                plugin.PluginDirectory = _pluginDirectory;
                _logger.LogInformation("Successfully loaded plugin {PluginId} in isolated context", plugin.PluginId);
                return plugin;
            }

            _logger.LogWarning("Failed to create plugin instance from {TypeName}", typeName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load plugin in isolated context: {AssemblyPath}", assemblyPath);
            return null;
        }
    }

    private async Task<IModPlugin?> CreatePluginInstanceAsync(Type pluginType)
    {
        try
        {
            // Try constructors with minimal dependencies
            var constructors = pluginType.GetConstructors()
                .OrderBy(c => c.GetParameters().Length);

            foreach (var constructor in constructors)
            {
                var parameters = constructor.GetParameters();
                var args = new object[parameters.Length];

                bool canCreate = true;
                for (int i = 0; i < parameters.Length; i++)
                {
                    var paramType = parameters[i].ParameterType;
                    
                    if (paramType.Name.Contains("ILogger") || 
                        (paramType.IsGenericType && paramType.GetGenericTypeDefinition().Name.Contains("ILogger")))
                    {
                        args[i] = new IsolatedNullLogger();
                    }
                    else if (paramType == typeof(HttpClient))
                    {
                        args[i] = CreateRestrictedHttpClient();
                    }
                    else if (paramType == typeof(string))
                    {
                        var paramName = parameters[i].Name?.ToLowerInvariant() ?? "";
                        if (paramName.Contains("directory") || paramName.Contains("path"))
                        {
                            args[i] = _pluginDirectory;
                        }
                        else
                        {
                            args[i] = string.Empty;
                        }
                    }
                    else if (paramType == typeof(TimeSpan) || paramType == typeof(TimeSpan?))
                    {
                        args[i] = TimeSpan.FromMinutes(30);
                    }
                    else if (parameters[i].HasDefaultValue)
                    {
                        args[i] = parameters[i].DefaultValue;
                    }
                    else if (paramType.IsValueType)
                    {
                        args[i] = Activator.CreateInstance(paramType);
                    }
                    else
                    {
                        args[i] = null;
                    }
                }

                if (canCreate)
                {
                    var instance = Activator.CreateInstance(pluginType, args) as IModPlugin;
                    if (instance != null)
                    {
                        return instance;
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating plugin instance");
            return null;
        }
    }

    private static HttpClient CreateRestrictedHttpClient()
    {
        var handler = new HttpClientHandler();
        var client = new HttpClient(handler);
        
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Add("User-Agent", "PluginManager-Plugin/1.0");
        
        return client;
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            _loadContext?.Unload();
            _loadContext = null;
            _logger.LogDebug("Unloaded plugin assembly load context");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unloading plugin assembly context");
        }
        finally
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// Custom AssemblyLoadContext for plugin isolation
/// </summary>
public class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly ILogger _logger;

    public PluginLoadContext(string pluginPath, ILogger logger) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
        _logger = logger;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Try to resolve the assembly path
        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            _logger.LogDebug("Loading assembly {AssemblyName} from {Path}", assemblyName.Name, assemblyPath);
            return LoadFromAssemblyPath(assemblyPath);
        }

        // Let the default context handle system assemblies
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }
}

/// <summary>
/// Safe logger implementation for isolated plugins
/// </summary>
internal class IsolatedNullLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}