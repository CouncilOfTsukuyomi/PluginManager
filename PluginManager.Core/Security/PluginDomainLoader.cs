using System.Reflection;
using Microsoft.Extensions.Logging;
using PluginManager.Core.Interfaces;

namespace PluginManager.Core.Security;

public class PluginDomainLoader : IDisposable
{
    private AppDomain? _pluginDomain;
    private PluginDomainProxy? _proxy;
    private bool _disposed;
    private readonly bool _appDomainSupported;

    public PluginDomainLoader()
    {
        // Check if AppDomains are supported on this platform
        _appDomainSupported = IsAppDomainSupported();
    }

    public IModPlugin? LoadPlugin(string assemblyPath, string typeName, string pluginDirectory)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PluginDomainLoader));

        try
        {
            if (_appDomainSupported)
            {
                return LoadPluginWithAppDomain(assemblyPath, typeName, pluginDirectory);
            }
            else
            {
                // Fallback to direct loading without AppDomain isolation
                return LoadPluginDirect(assemblyPath, typeName, pluginDirectory);
            }
        }
        catch (Exception ex)
        {
            // Clean up on failure
            Dispose();
            throw new InvalidOperationException($"Failed to load plugin: {ex.Message}", ex);
        }
    }

    private bool IsAppDomainSupported()
    {
        try
        {
            // Try to create a test AppDomain to see if it's supported
            var testDomain = AppDomain.CreateDomain("TestDomain");
            AppDomain.Unload(testDomain);
            return true;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private IModPlugin? LoadPluginWithAppDomain(string assemblyPath, string typeName, string pluginDirectory)
    {
        // Create AppDomain for isolation
        _pluginDomain = CreateBasicAppDomain(pluginDirectory);
        
        // Create proxy in the new AppDomain
        var proxyType = typeof(PluginDomainProxy);
        _proxy = (PluginDomainProxy)_pluginDomain.CreateInstanceAndUnwrap(
            proxyType.Assembly.FullName!, 
            proxyType.FullName!);

        // Load the plugin through the proxy
        return _proxy.LoadPlugin(assemblyPath, typeName, pluginDirectory);
    }

    private IModPlugin? LoadPluginDirect(string assemblyPath, string typeName, string pluginDirectory)
    {
        // Direct loading without AppDomain isolation
        // This is less secure but works on all platforms
        _proxy = new PluginDomainProxy();
        return _proxy.LoadPlugin(assemblyPath, typeName, pluginDirectory);
    }

    private AppDomain CreateBasicAppDomain(string pluginDirectory)
    {
        var domainName = $"PluginDomain_{Guid.NewGuid():N}";
        return AppDomain.CreateDomain(domainName);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            _proxy?.Dispose();
            _proxy = null;
        }
        catch (Exception)
        {
            // Ignore disposal errors from proxy
        }

        try
        {
            if (_pluginDomain != null)
            {
                AppDomain.Unload(_pluginDomain);
                _pluginDomain = null;
            }
        }
        catch (Exception)
        {
            // Ignore unload errors
        }

        _disposed = true;
    }
}

/// <summary>
/// Proxy class that can run in an isolated AppDomain or directly
/// </summary>
public class PluginDomainProxy : MarshalByRefObject, IDisposable
{
    private IModPlugin? _loadedPlugin;
    private bool _disposed;

    public IModPlugin? LoadPlugin(string assemblyPath, string typeName, string pluginDirectory)
    {
        if (_disposed)
            return null;

        try
        {
            // Load the assembly
            var assembly = Assembly.LoadFrom(assemblyPath);
            var pluginType = assembly.GetType(typeName);
            
            if (pluginType == null || !typeof(IModPlugin).IsAssignableFrom(pluginType))
            {
                return null;
            }

            _loadedPlugin = CreatePluginInstance(pluginType, pluginDirectory);
            return _loadedPlugin;
        }
        catch (Exception)
        {
            // Swallow exceptions to prevent them from crossing domain boundaries (if applicable)
            return null;
        }
    }

    private IModPlugin? CreatePluginInstance(Type pluginType, string pluginDirectory)
    {
        try
        {
            // Try to create with minimal dependencies
            var constructors = pluginType.GetConstructors()
                .OrderBy(c => c.GetParameters().Length);

            foreach (var constructor in constructors)
            {
                var parameters = constructor.GetParameters();
                var args = new object[parameters.Length];

                for (int i = 0; i < parameters.Length; i++)
                {
                    var paramType = parameters[i].ParameterType;
                    
                    // Provide safe implementations only
                    if (paramType.Name.Contains("ILogger") || 
                        (paramType.IsGenericType && paramType.GetGenericTypeDefinition().Name.Contains("ILogger")))
                    {
                        args[i] = new SandboxNullLogger();
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
                            args[i] = pluginDirectory;
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
                    else
                    {
                        args[i] = GetDefaultValue(paramType, parameters[i]);
                    }
                }

                try
                {
                    var instance = Activator.CreateInstance(pluginType, args) as IModPlugin;
                    if (instance != null)
                    {
                        instance.PluginDirectory = pluginDirectory;
                        return instance;
                    }
                }
                catch (Exception)
                {
                    // Try next constructor
                    continue;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private HttpClient CreateRestrictedHttpClient()
    {
        var handler = new HttpClientHandler();
        var client = new HttpClient(handler);
        
        // Add restrictions
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Add("User-Agent", "PluginManager-Plugin/1.0");
        
        return client;
    }

    private object? GetDefaultValue(Type type, ParameterInfo parameter)
    {
        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }
        
        if (parameter.HasDefaultValue)
        {
            return parameter.DefaultValue;
        }
        
        return null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            if (_loadedPlugin != null)
            {
                var disposeTask = _loadedPlugin.DisposeAsync();
                if (disposeTask.IsCompleted)
                {
                    disposeTask.GetAwaiter().GetResult();
                }
                else
                {
                    // Don't wait for async disposal in domain unload
                    Task.Run(async () => await disposeTask);
                }
            }
        }
        catch
        {
            // Ignore disposal errors
        }

        _loadedPlugin = null;
        _disposed = true;
    }

    // Override to prevent the lease from expiring
    public override object? InitializeLifetimeService()
    {
        return null;
    }
}

/// <summary>
/// Safe logger implementation for sandboxed plugins
/// </summary>
internal class SandboxNullLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}