
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;

namespace PluginManager.Core.Security;

public class IsolatedPluginLoader : IDisposable
{
    private readonly ILogger<IsolatedPluginLoader> _logger;
    private readonly string _pluginDirectory;
    private PluginLoadContext? _loadContext;
    private WeakReference? _loadContextWeakRef;
    private bool _disposed;
    private bool _unloadRequested;

    // Add properties to check unload state
    public bool IsUnloaded => _loadContextWeakRef?.IsAlive == false;
    public bool UnloadRequested => _unloadRequested;

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

            // Check if the type implements IModPlugin by interface name (avoids type identity issues)
            bool implementsIModPlugin = pluginType.GetInterfaces()
                .Any(i => i.Name == nameof(IModPlugin) && i.Namespace == typeof(IModPlugin).Namespace);

            if (!implementsIModPlugin)
            {
                _logger.LogWarning("Type {TypeName} doesn't implement IModPlugin interface. Interfaces found: {Interfaces}", 
                    typeName, string.Join(", ", pluginType.GetInterfaces().Select(i => $"{i.Namespace}.{i.Name}")));
                return null;
            }

            _logger.LogDebug("Type {TypeName} implements IModPlugin, creating instance...", typeName);

            var pluginInstance = await CreatePluginInstanceAsync(pluginType);
            
            if (pluginInstance != null)
            {
                // Create a proxy wrapper to handle the type identity issue
                var proxy = new IsolatedPluginProxy(pluginInstance, _pluginDirectory, _logger);
                _logger.LogInformation("Successfully loaded plugin {PluginId} in isolated context", proxy.PluginId);
                return proxy;
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

    public async Task<bool> WaitForUnloadAsync(TimeSpan timeout)
    {
        if (_loadContextWeakRef == null)
            return true; // Never loaded or already collected

        var cancellationToken = new CancellationTokenSource(timeout).Token;
        
        try
        {
            while (_loadContextWeakRef.IsAlive && !cancellationToken.IsCancellationRequested)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                if (_loadContextWeakRef.IsAlive)
                {
                    await Task.Delay(100, cancellationToken);
                }
            }
            
            bool isUnloaded = !_loadContextWeakRef.IsAlive;
            _logger.LogDebug("Plugin unload wait completed. IsUnloaded: {IsUnloaded}", isUnloaded);
            return isUnloaded;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Timeout waiting for plugin assembly context to unload");
            return false;
        }
    }

    private async Task<object?> CreatePluginInstanceAsync(Type pluginType)
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

                    var instance = Activator.CreateInstance(pluginType, args);
                    if (instance != null)
                    {
                        _logger.LogInformation("Successfully created plugin instance of type {TypeName}", pluginType.Name);
                        return instance;
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

            _logger.LogDebug("Processing parameter {Index}: {Type} {Name} (Assembly: {Assembly})", 
                i, paramType.Name, paramName, paramType.Assembly.GetName().Name);

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
            _logger.LogDebug("Attempting to create argument for parameter type: {FullName} (Assembly: {Assembly})", 
                paramType.FullName, paramType.Assembly.GetName().Name);

            // Handle ILogger types by checking the actual type name and assembly
            if (IsLoggerType(paramType))
            {
                _logger.LogDebug("Detected logger type: {TypeName}", paramType.FullName);
                
                // Create a logger instance that exists in the same assembly context as the parameter
                argument = CreateLoggerForContext(paramType);
                if (argument != null)
                {
                    _logger.LogDebug("Successfully created logger argument");
                    return true;
                }
            }

            if (paramType.Name == "HttpClient" && paramType.Namespace == "System.Net.Http")
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

            if (paramType.Name == "TimeSpan" && paramType.Namespace == "System")
            {
                argument = TimeSpan.FromMinutes(30);
                return true;
            }

            if (paramType.Name == "Int32" && paramType.Namespace == "System")
            {
                argument = 0;
                return true;
            }

            if (paramType.Name == "Boolean" && paramType.Namespace == "System")
            {
                argument = false;
                return true;
            }

            if (paramType.Name == "CancellationToken" && paramType.Namespace == "System.Threading")
            {
                argument = CancellationToken.None;
                return true;
            }

            // Handle Dictionary<string, object> for configuration
            if (paramType.Name.StartsWith("Dictionary") && paramType.Namespace == "System.Collections.Generic")
            {
                var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(string), typeof(object));
                argument = Activator.CreateInstance(dictType);
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
                _logger.LogDebug("Cannot create instance of interface/abstract type: {TypeName}", paramType.Name);
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

    private static bool IsLoggerType(Type type)
    {
        // Check for ILogger interface by name (works across assembly contexts)
        return (type.Name == "ILogger" && type.Namespace == "Microsoft.Extensions.Logging") ||
               (type.IsGenericType && 
                type.GetGenericTypeDefinition().Name == "ILogger`1" && 
                type.GetGenericTypeDefinition().Namespace == "Microsoft.Extensions.Logging");
    }

    private object? CreateLoggerForContext(Type loggerType)
    {
        try
        {
            // Find the NullLogger type in the same assembly as the logger parameter
            var assembly = loggerType.Assembly;
            
            if (loggerType.Name == "ILogger" && !loggerType.IsGenericType)
            {
                // Non-generic ILogger - find NullLogger in Microsoft.Extensions.Logging.Abstractions
                var nullLoggerType = assembly.GetType("Microsoft.Extensions.Logging.Abstractions.NullLogger");
                if (nullLoggerType != null)
                {
                    var instanceProperty = nullLoggerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    if (instanceProperty != null)
                    {
                        return instanceProperty.GetValue(null);
                    }
                }
                
                // Fallback: try to create a simple null logger implementation
                var nullLoggerImplType = assembly.GetTypes()
                    .FirstOrDefault(t => t.Name.Contains("NullLogger") && !t.IsGenericType);
                
                if (nullLoggerImplType != null && nullLoggerImplType.GetConstructor(Type.EmptyTypes) != null)
                {
                    return Activator.CreateInstance(nullLoggerImplType);
                }
            }
            else if (loggerType.IsGenericType)
            {
                // Generic ILogger<T> - find NullLogger<T>
                var genericArg = loggerType.GetGenericArguments()[0];
                var nullLoggerGenericType = assembly.GetType("Microsoft.Extensions.Logging.Abstractions.NullLogger`1");
                
                if (nullLoggerGenericType != null)
                {
                    var specificNullLoggerType = nullLoggerGenericType.MakeGenericType(genericArg);
                    var instanceProperty = specificNullLoggerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    if (instanceProperty != null)
                    {
                        return instanceProperty.GetValue(null);
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to create logger for context");
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
            if (_loadContext != null)
            {
                _logger.LogDebug("Requesting unload of plugin assembly load context");
                
                // Create weak reference to track unloading
                _loadContextWeakRef = new WeakReference(_loadContext);
                
                // Request unload
                _loadContext.Unload();
                _unloadRequested = true;
                _loadContext = null;

                _logger.LogDebug("Plugin assembly context unload requested");
            }
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