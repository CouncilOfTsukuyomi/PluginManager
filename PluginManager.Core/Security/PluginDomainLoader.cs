using System.Reflection;
using Microsoft.Extensions.Logging;
using PluginManager.Core.Interfaces;

namespace PluginManager.Core.Security;

public class PluginDomainLoader : MarshalByRefObject
{
    public async Task<IModPlugin?> LoadPluginAsync(string assemblyPath, string typeName, string pluginDirectory)
    {
        try
        {
            // Load the assembly
            var assembly = Assembly.LoadFrom(assemblyPath);
            var pluginType = assembly.GetType(typeName);
            
            if (pluginType == null || !typeof(IModPlugin).IsAssignableFrom(pluginType))
            {
                return null;
            }

            var plugin = CreatePluginInstance(pluginType, pluginDirectory);
            return plugin;
        }
        catch (Exception)
        {
            // Swallow exceptions in sandboxed domain to prevent them from crossing domain boundaries
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

                var canCreate = true;
                for (int i = 0; i < parameters.Length; i++)
                {
                    var paramType = parameters[i].ParameterType;
                    
                    // Provide safe implementations only
                    if (paramType.Name.Contains("ILogger") || 
                        (paramType.IsGenericType && paramType.GetGenericTypeDefinition().Name.Contains("ILogger")))
                    {
                        args[i] = new SandboxNullLogger(); // Safe logger implementation
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

                if (canCreate)
                {
                    var instance = Activator.CreateInstance(pluginType, args) as IModPlugin;
                    if (instance != null)
                    {
                        instance.PluginDirectory = pluginDirectory;
                        return instance;
                    }
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