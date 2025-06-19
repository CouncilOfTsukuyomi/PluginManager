
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
/// Proxy that wraps an isolated plugin instance and implements IModPlugin in the main context
/// This solves the type identity issue where interfaces from different assembly contexts are incompatible
/// </summary>
internal class IsolatedPluginProxy : IModPlugin
{
    private readonly object _pluginInstance;
    private readonly Type _pluginType;
    private readonly ILogger _logger;

    public IsolatedPluginProxy(object pluginInstance, string pluginDirectory, ILogger logger)
    {
        _pluginInstance = pluginInstance ?? throw new ArgumentNullException(nameof(pluginInstance));
        _pluginType = pluginInstance.GetType();
        _logger = logger;
        PluginDirectory = pluginDirectory;
    }

    public string PluginId => GetStringProperty("PluginId");
    public string DisplayName => GetStringProperty("DisplayName");
    public string Description => GetStringProperty("Description");
    public string Version => GetStringProperty("Version");
    public string Author => GetStringProperty("Author");
    
    public bool IsEnabled 
    { 
        get => GetBoolProperty("IsEnabled");
        set => SetBoolProperty("IsEnabled", value);
    }
    
    public string PluginDirectory 
    { 
        get => GetStringProperty("PluginDirectory");
        set => SetStringProperty("PluginDirectory", value);
    }

    public async Task<List<PluginMod>> GetRecentModsAsync()
    {
        try
        {
            var method = _pluginType.GetMethod("GetRecentModsAsync");
            if (method != null)
            {
                var result = method.Invoke(_pluginInstance, null);
                if (result is Task task)
                {
                    await task;
                    
                    // Get the result from the completed task
                    var resultProperty = task.GetType().GetProperty("Result");
                    if (resultProperty?.GetValue(task) is IEnumerable<object> pluginMods)
                    {
                        return ConvertToPluginMods(pluginMods);
                    }
                }
            }
            
            return new List<PluginMod>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling GetRecentModsAsync on isolated plugin");
            return new List<PluginMod>();
        }
    }

    public async Task InitializeAsync(Dictionary<string, object> configuration)
    {
        try
        {
            var method = _pluginType.GetMethod("InitializeAsync");
            if (method != null)
            {
                var result = method.Invoke(_pluginInstance, new object[] { configuration });
                if (result is Task task)
                {
                    await task;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling InitializeAsync on isolated plugin");
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            var method = _pluginType.GetMethod("DisposeAsync");
            if (method != null)
            {
                var result = method.Invoke(_pluginInstance, null);
                if (result is ValueTask valueTask)
                {
                    await valueTask;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling DisposeAsync on isolated plugin");
        }
    }

    private string GetStringProperty(string propertyName)
    {
        try
        {
            var property = _pluginType.GetProperty(propertyName);
            return property?.GetValue(_pluginInstance)?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private bool GetBoolProperty(string propertyName)
    {
        try
        {
            var property = _pluginType.GetProperty(propertyName);
            return (bool)(property?.GetValue(_pluginInstance) ?? false);
        }
        catch
        {
            return false;
        }
    }

    private void SetBoolProperty(string propertyName, bool value)
    {
        try
        {
            var property = _pluginType.GetProperty(propertyName);
            property?.SetValue(_pluginInstance, value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set property {PropertyName}", propertyName);
        }
    }

    private void SetStringProperty(string propertyName, string value)
    {
        try
        {
            var property = _pluginType.GetProperty(propertyName);
            property?.SetValue(_pluginInstance, value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set property {PropertyName}", propertyName);
        }
    }

    private List<PluginMod> ConvertToPluginMods(IEnumerable<object> sourcePluginMods)
    {
        var result = new List<PluginMod>();
        
        foreach (var sourceMod in sourcePluginMods)
        {
            try
            {
                var sourceType = sourceMod.GetType();
                var pluginMod = new PluginMod();

                // Copy properties by name using reflection
                CopyProperty(sourceMod, pluginMod, "Name");
                CopyProperty(sourceMod, pluginMod, "Author");
                CopyProperty(sourceMod, pluginMod, "Description");
                CopyProperty(sourceMod, pluginMod, "ModUrl");
                CopyProperty(sourceMod, pluginMod, "DownloadUrl");
                CopyProperty(sourceMod, pluginMod, "ImageUrl");
                CopyProperty(sourceMod, pluginMod, "Version");
                CopyProperty(sourceMod, pluginMod, "Tags");
                CopyProperty(sourceMod, pluginMod, "PluginSource");
                
                result.Add(pluginMod);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to convert plugin mod object");
            }
        }

        return result;
    }

    private void CopyProperty(object source, object target, string propertyName)
    {
        try
        {
            var sourceProp = source.GetType().GetProperty(propertyName);
            var targetProp = target.GetType().GetProperty(propertyName);
            
            if (sourceProp != null && targetProp != null && targetProp.CanWrite)
            {
                var value = sourceProp.GetValue(source);
                targetProp.SetValue(target, value);
            }
        }
        catch
        {
            // Ignore property copy failures
        }
    }
}