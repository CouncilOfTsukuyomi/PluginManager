
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
            
            if (pluginType == null)
            {
                _logger.LogWarning("Type {TypeName} not found in assembly {AssemblyPath}", typeName, assemblyPath);
                return null;
            }

            _logger.LogDebug("Found type {TypeName}, checking if it implements IModPlugin...", typeName);

            // Instead of using typeof(IModPlugin).IsAssignableFrom, check by interface name
            // This avoids type identity issues across assembly contexts
            bool implementsIModPlugin = pluginType.GetInterfaces()
                .Any(i => i.Name == nameof(IModPlugin) && i.Namespace == typeof(IModPlugin).Namespace);

            if (!implementsIModPlugin)
            {
                _logger.LogWarning("Type {TypeName} doesn't implement IModPlugin interface. Interfaces found: {Interfaces}", 
                    typeName, string.Join(", ", pluginType.GetInterfaces().Select(i => $"{i.Namespace}.{i.Name}")));
                return null;
            }

            _logger.LogDebug("Type {TypeName} implements IModPlugin, creating instance...", typeName);

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
            _logger.LogDebug("Attempting to create instance of {TypeName}", pluginType.FullName);

            // Get all constructors and try them in order of preference
            var constructors = pluginType.GetConstructors()
                .OrderBy(c => c.GetParameters().Length) // Try parameterless first
                .ToArray();

            _logger.LogDebug("Found {ConstructorCount} constructors for {TypeName}", constructors.Length, pluginType.Name);

            foreach (var constructor in constructors)
            {
                var parameters = constructor.GetParameters();
                _logger.LogDebug("Trying constructor with {ParameterCount} parameters: [{Parameters}]", 
                    parameters.Length, 
                    string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}")));

                try
                {
                    var args = CreateConstructorArguments(parameters);
                    if (args == null)
                    {
                        _logger.LogDebug("Skipping constructor - couldn't create suitable arguments");
                        continue;
                    }

                    _logger.LogDebug("Invoking constructor with arguments: [{Arguments}]", 
                        string.Join(", ", args.Select((arg, i) => $"{parameters[i].Name}={arg?.GetType().Name ?? "null"}")));

                    var instance = Activator.CreateInstance(pluginType, args) as IModPlugin;
                    if (instance != null)
                    {
                        _logger.LogInformation("Successfully created plugin instance: {PluginId}", instance.PluginId);
                        return instance;
                    }
                    else
                    {
                        _logger.LogWarning("Created instance but cast to IModPlugin failed - this suggests type identity issues");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Constructor with {ParameterCount} parameters failed: {Error}", parameters.Length, ex.Message);
                    // Continue to next constructor
                }
            }

            _logger.LogError("No suitable constructor found for {TypeName}", pluginType.FullName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating plugin instance");
            return null;
        }
    }

    private object[]? CreateConstructorArguments(ParameterInfo[] parameters)
    {
        var args = new object[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            var paramType = param.ParameterType;
            var paramName = param.Name?.ToLowerInvariant() ?? "";

            _logger.LogDebug("Processing parameter {Index}: {Type} {Name}", i, paramType.Name, paramName);

            // Try to create appropriate argument based on parameter type
            if (TryCreateArgument(paramType, paramName, out var argument))
            {
                args[i] = argument;
                _logger.LogDebug("Created argument for {Name}: {Value}", paramName, argument?.GetType().Name ?? "null");
            }
            else if (param.HasDefaultValue)
            {
                args[i] = param.DefaultValue;
                _logger.LogDebug("Using default value for {Name}: {Value}", paramName, param.DefaultValue);
            }
            else if (paramType.IsValueType)
            {
                args[i] = Activator.CreateInstance(paramType);
                _logger.LogDebug("Created default value type for {Name}: {Value}", paramName, args[i]);
            }
            else
            {
                // Can't create a suitable argument for this constructor
                _logger.LogDebug("Cannot create argument for required parameter {Name} of type {Type}", paramName, paramType.Name);
                return null;
            }
        }

        return args;
    }

    private bool TryCreateArgument(Type paramType, string paramName, out object? argument)
    {
        argument = null;

        try
        {
            // Handle specific ILogger types
            if (paramType == typeof(ILogger))
            {
                // Non-generic ILogger - create a wrapper
                argument = new NonGenericLoggerWrapper();
                _logger.LogDebug("Created NonGenericLoggerWrapper for ILogger parameter");
                return true;
            }

            if (IsGenericLogger(paramType))
            {
                // Generic ILogger<T> - use our null logger
                argument = new IsolatedNullLogger();
                _logger.LogDebug("Created IsolatedNullLogger for ILogger<T> parameter");
                return true;
            }

            if (paramType == typeof(HttpClient))
            {
                argument = CreateRestrictedHttpClient();
                return true;
            }

            if (paramType == typeof(string))
            {
                if (paramName.Contains("directory") || paramName.Contains("path"))
                {
                    argument = _pluginDirectory;
                }
                else if (paramName.Contains("url") || paramName.Contains("baseurl") || paramName.Contains("endpoint"))
                {
                    argument = "https://example.com"; // Safe default URL
                }
                else
                {
                    argument = string.Empty;
                }
                return true;
            }

            if (paramType == typeof(TimeSpan) || paramType == typeof(TimeSpan?))
            {
                argument = TimeSpan.FromMinutes(30);
                return true;
            }

            if (paramType == typeof(int) || paramType == typeof(int?))
            {
                argument = 0;
                return true;
            }

            if (paramType == typeof(bool) || paramType == typeof(bool?))
            {
                argument = false;
                return true;
            }

            if (paramType == typeof(CancellationToken))
            {
                argument = CancellationToken.None;
                return true;
            }

            // Handle Dictionary<string, object> for configuration
            if (paramType == typeof(Dictionary<string, object>))
            {
                argument = new Dictionary<string, object>();
                return true;
            }

            // For nullable types, return null
            if (Nullable.GetUnderlyingType(paramType) != null)
            {
                argument = null;
                return true;
            }

            // For value types, try to create default instance
            if (paramType.IsValueType)
            {
                argument = Activator.CreateInstance(paramType);
                return true;
            }

            // For interfaces or abstract classes, can't create instances
            if (paramType.IsInterface || paramType.IsAbstract)
            {
                return false;
            }

            // Try to create instance with parameterless constructor
            if (paramType.GetConstructor(Type.EmptyTypes) != null)
            {
                argument = Activator.CreateInstance(paramType);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to create argument for parameter type {Type}", paramType.Name);
            return false;
        }
    }

    private static bool IsGenericLogger(Type type)
    {
        return type.IsGenericType && 
               type.GetGenericTypeDefinition() == typeof(ILogger<>);
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
/// Safe logger implementation for isolated plugins that implements generic ILogger<T>
/// </summary>
internal class IsolatedNullLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}

/// <summary>
/// Wrapper that implements non-generic ILogger interface for plugins that expect the old-style logger
/// </summary>
internal class NonGenericLoggerWrapper : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}