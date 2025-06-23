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
    private WeakReference? _loadContextRef;
    private IModPlugin? _plugin;
    private bool _disposed;
    private readonly object _lock = new object();

    public bool UnloadRequested { get; private set; }
    public bool IsUnloaded => _loadContextRef?.IsAlive == false;

    public IsolatedPluginLoader(ILogger<IsolatedPluginLoader> logger, string pluginDirectory)
    {
        _logger = logger;
        _pluginDirectory = pluginDirectory;
    }

    public async Task<IModPlugin?> LoadPluginAsync(string assemblyPath, string typeName)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(IsolatedPluginLoader));

        lock (_lock)
        {
            try
            {
                _logger.LogDebug("Creating isolated load context for plugin assembly: {AssemblyPath}", assemblyPath);
                
                // CRITICAL: Pre-load PluginManager.Core into the default context
                // This ensures it's available when the plugin context defers to the default context
                EnsurePluginManagerCoreLoaded();
            
                _loadContext = new PluginLoadContext(assemblyPath, _logger);
                _loadContextRef = new WeakReference(_loadContext, trackResurrection: true);

                // Load the plugin assembly into the isolated context
                var assembly = _loadContext.LoadFromAssemblyPath(assemblyPath);
            
                // Add diagnostics to see what types are actually available
                _logger.LogDebug("Assembly {AssemblyName} loaded successfully", assembly.FullName);
                var availableTypes = assembly.GetTypes().Select(t => t.FullName).ToList();
                _logger.LogDebug("Available types in assembly: {Types}", string.Join(", ", availableTypes));
            
                var pluginType = assembly.GetType(typeName);

                if (pluginType == null)
                {
                    _logger.LogError("Type {TypeName} not found in assembly. Available types: {AvailableTypes}", 
                        typeName, string.Join(", ", availableTypes));
                    throw new InvalidOperationException($"Type {typeName} not found in assembly {assemblyPath}");
                }

                // Check if the type implements IModPlugin directly or through inheritance
                if (!ImplementsIModPluginInterface(pluginType))
                {
                    var interfaces = pluginType.GetInterfaces().Select(i => i.FullName);
                    var baseTypes = GetInheritanceChain(pluginType).Select(t => t.FullName);
                    _logger.LogError("Type {TypeName} does not implement IModPlugin. Implemented interfaces: {Interfaces}, Base types: {BaseTypes}", 
                        typeName, string.Join(", ", interfaces), string.Join(", ", baseTypes));
                    throw new InvalidOperationException($"Type {typeName} does not implement IModPlugin");
                }

                // Create plugin instance
                _plugin = CreatePluginInstance(pluginType);
                if (_plugin != null)
                {
                    _plugin.PluginDirectory = _pluginDirectory;
                }

                _logger.LogInformation("Successfully loaded plugin {TypeName} in isolated context", typeName);
                return _plugin;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load plugin {TypeName} from {AssemblyPath}", typeName, assemblyPath);
            
                // Clean up on failure
                Dispose();
                throw;
            }
        }
    }

    private void EnsurePluginManagerCoreLoaded()
    {
        try
        {
            // Force load PluginManager.Core into the default context
            // This ensures it's available when the plugin context defers to it
            var pluginManagerCoreAssembly = typeof(IModPlugin).Assembly;
        
            // Also force load by accessing the assembly through different means
            var assemblyName = pluginManagerCoreAssembly.GetName();
        
            _logger.LogDebug("Pre-loaded PluginManager.Core into default context: {Assembly} (Version: {Version})", 
                pluginManagerCoreAssembly.FullName, assemblyName.Version);
        
            // Verify it's in the default context's loaded assemblies
            var isInDefaultContext = AssemblyLoadContext.Default.Assemblies
                .Any(a => string.Equals(a.GetName().Name, "PluginManager.Core", StringComparison.OrdinalIgnoreCase));
            
            _logger.LogDebug("PluginManager.Core is in default context: {IsLoaded}", isInDefaultContext);
        
            // Also ensure the current assembly (which contains IsolatedPluginLoader) is loaded
            var currentAssembly = typeof(IsolatedPluginLoader).Assembly;
            _logger.LogDebug("Current assembly in default context: {Assembly} (Version: {Version})", 
                currentAssembly.FullName, currentAssembly.GetName().Version);
            
            // Verify IModPlugin is accessible
            var imodPluginType = typeof(IModPlugin);
            _logger.LogDebug("IModPlugin type loaded from: {Assembly} (Location: {Location}, Version: {Version})", 
                imodPluginType.Assembly.FullName, imodPluginType.Assembly.Location, imodPluginType.Assembly.GetName().Version);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to pre-load PluginManager.Core - this may cause assembly resolution issues");
        }
    }


    private bool ImplementsIModPluginInterface(Type pluginType)
    {
        // Check the entire inheritance chain for IModPlugin implementation
        var typesToCheck = GetInheritanceChain(pluginType);
    
        foreach (var type in typesToCheck)
        {
            // Check direct interface implementation
            var interfaces = type.GetInterfaces();
            foreach (var @interface in interfaces)
            {
                if (IsIModPluginInterface(@interface))
                {
                    _logger.LogDebug("Found IModPlugin interface implementation on type {TypeName} via interface {InterfaceName}", 
                        pluginType.FullName, @interface.FullName);
                    return true;
                }
            }
        }
    
        return false;
    }

    private bool IsIModPluginInterface(Type interfaceType)
    {
        // Check by name and structure since type identity might be different across contexts
        return (interfaceType.Name == "IModPlugin" || 
                interfaceType.FullName == "PluginManager.Core.Interfaces.IModPlugin") &&
               VerifyIModPluginStructure(interfaceType);
    }

    private IEnumerable<Type> GetInheritanceChain(Type type)
    {
        var current = type;
        while (current != null)
        {
            yield return current;
            current = current.BaseType;
        }
    }

    private bool VerifyIModPluginStructure(Type interfaceType)
    {
        try
        {
            // Check for key methods that should exist in IModPlugin based on the actual interface
            var expectedMethods = new[]
            {
                "get_PluginId",        
                "get_DisplayName",     
                "get_Description",     
                "get_Version",         
                "get_Author",          
                "get_IsEnabled",       
                "set_IsEnabled",       
                "get_PluginDirectory",
                "set_PluginDirectory",
                "InitializeAsync",
                "DisposeAsync",
                "RequestCancellation"
            };
        
            var actualMethods = interfaceType.GetMethods().Select(m => m.Name).ToHashSet();
            
            // Check if core required methods are present (more lenient check)
            var coreRequiredMethods = new[]
            {
                "get_PluginId",
                "get_PluginDirectory", 
                "set_PluginDirectory",
                "InitializeAsync",
                "DisposeAsync",
                "RequestCancellation"
            };
            
            bool hasCoreMethods = coreRequiredMethods.All(expectedMethod => actualMethods.Contains(expectedMethod));
            
            if (!hasCoreMethods)
            {
                var missingMethods = coreRequiredMethods.Where(m => !actualMethods.Contains(m));
                _logger.LogDebug("Interface {InterfaceType} missing core methods: {Missing}. All methods: {Actual}", 
                    interfaceType.FullName, string.Join(", ", missingMethods), string.Join(", ", actualMethods));
            }
            
            return hasCoreMethods;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify IModPlugin structure for interface {InterfaceType}", interfaceType.FullName);
            return false;
        }
    }

    private IModPlugin? CreatePluginInstance(Type pluginType)
    {
        var constructors = pluginType.GetConstructors()
            .OrderBy(c => c.GetParameters().Length);

        Exception? lastException = null;

        foreach (var constructor in constructors)
        {
            try
            {
                var parameters = constructor.GetParameters();
                var args = new object?[parameters.Length];
                bool canCreateInstance = true;

                _logger.LogDebug("Attempting to create instance with constructor: {Constructor}", 
                    string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}")));

                for (int i = 0; i < parameters.Length; i++)
                {
                    var paramType = parameters[i].ParameterType;
                    var paramValue = CreateParameterValue(paramType, parameters[i]);
                    
                    // For reference types, null is acceptable unless explicitly marked as non-nullable
                    if (paramValue == null && !parameters[i].HasDefaultValue)
                    {
                        // For value types, we need a value
                        if (paramType.IsValueType && !IsNullableValueType(paramType))
                        {
                            _logger.LogDebug("Cannot provide value for required value type parameter {ParameterName} of type {ParameterType}", 
                                parameters[i].Name, paramType.Name);
                            canCreateInstance = false;
                            break;
                        }
                        
                        // For reference types like interfaces, null is often acceptable
                        // but let's be more careful with logger types since they're critical
                        if (IsLoggerType(paramType))
                        {
                            _logger.LogDebug("Cannot provide logger for parameter {ParameterName} of type {ParameterType}", 
                                parameters[i].Name, paramType.Name);
                            canCreateInstance = false;
                            break;
                        }
                    }
                    
                    args[i] = paramValue;
                    
                    _logger.LogDebug("Parameter {ParameterName} ({ParameterType}): {Value}", 
                        parameters[i].Name, paramType.Name, paramValue?.GetType().Name ?? "null");
                }

                if (!canCreateInstance)
                {
                    _logger.LogDebug("Skipping constructor due to missing required parameters");
                    continue;
                }

                _logger.LogDebug("Attempting to create instance with {ParameterCount} parameters", parameters.Length);
                var instance = Activator.CreateInstance(pluginType, args);
                
                if (instance is IModPlugin plugin)
                {
                    _logger.LogInformation("Successfully created plugin instance using constructor with {ParameterCount} parameters", 
                        parameters.Length);
                    return plugin;
                }
                else
                {
                    _logger.LogWarning("Created instance but it does not implement IModPlugin interface. Instance type: {InstanceType}", 
                        instance?.GetType().FullName ?? "null");
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogDebug(ex, "Failed to create plugin instance with constructor {Constructor}", 
                    string.Join(", ", constructor.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}")));
                continue;
            }
        }

        _logger.LogError("Failed to create plugin instance with any available constructor. Last error: {Error}", 
            lastException?.Message);
        return null;
    }

    private bool IsNullableValueType(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
    }

    private object? CreateParameterValue(Type paramType, ParameterInfo parameter)
    {
        // Handle ILogger parameters more robustly
        if (IsLoggerType(paramType))
        {
            return CreateLoggerInstance(paramType);
        }

        if (paramType == typeof(HttpClient))
        {
            return new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        }

        if (paramType == typeof(string))
        {
            var paramName = parameter.Name?.ToLowerInvariant() ?? "";
            if (paramName.Contains("directory") || paramName.Contains("path"))
            {
                return _pluginDirectory;
            }
            return string.Empty;
        }

        if (paramType == typeof(TimeSpan))
        {
            return TimeSpan.FromMinutes(30);
        }

        if (paramType == typeof(TimeSpan?))
        {
            return (TimeSpan?)TimeSpan.FromMinutes(30);
        }

        if (paramType.IsValueType)
        {
            return Activator.CreateInstance(paramType);
        }

        // Handle nullable value types
        if (IsNullableValueType(paramType))
        {
            var underlyingType = Nullable.GetUnderlyingType(paramType);
            if (underlyingType != null)
            {
                if (underlyingType == typeof(TimeSpan))
                {
                    return (TimeSpan?)TimeSpan.FromMinutes(30);
                }
                return Activator.CreateInstance(underlyingType);
            }
        }

        // Handle interfaces and abstract classes
        if (paramType.IsInterface || paramType.IsAbstract)
        {
            // Return null for interfaces/abstract classes we can't instantiate
            return null;
        }

        return parameter.HasDefaultValue ? parameter.DefaultValue : null;
    }

    private bool IsLoggerType(Type paramType)
    {
        return paramType.Name.Contains("ILogger") ||
           paramType.FullName?.Contains("Microsoft.Extensions.Logging") == true ||
           paramType.FullName?.Contains("ILogger") == true;
    }

    private object? CreateLoggerInstance(Type paramType)
    {
        try
        {
            // Handle generic ILogger<T>
            if (paramType.IsGenericType && paramType.GetGenericTypeDefinition().Name.Contains("ILogger"))
            {
                var genericArgs = paramType.GetGenericArguments();
                if (genericArgs.Length == 1)
                {
                    var loggerType = typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>)
                        .MakeGenericType(genericArgs[0]);
                    var instanceProperty = loggerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    if (instanceProperty != null)
                    {
                        return instanceProperty.GetValue(null);
                    }
                    return Activator.CreateInstance(loggerType);
                }
            }
        
            // Handle non-generic ILogger
            if (paramType.Name == "ILogger" && !paramType.IsGenericType)
            {
                return Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
            }
            
            // Try to create any available logger from the plugin's context
            var sandboxLoggerType = Type.GetType("PluginManager.Core.Security.SandboxNullLogger");
            if (sandboxLoggerType != null && paramType.IsAssignableFrom(sandboxLoggerType))
            {
                return Activator.CreateInstance(sandboxLoggerType);
            }
            
            // Try to find NullLogger in the same assembly context as the plugin
            var loggerAssembly = paramType.Assembly;
            var nullLoggerType = loggerAssembly.GetTypes()
                .FirstOrDefault(t => t.Name.Contains("NullLogger") && !t.IsGenericType);
                
            if (nullLoggerType != null)
            {
                var instanceProp = nullLoggerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProp != null)
                {
                    return instanceProp.GetValue(null);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to create logger instance for type {LoggerType}", paramType.FullName);
        }
    
        // Final fallback
        return Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
}

    public async Task<bool> WaitForUnloadAsync(TimeSpan timeout)
    {
        if (_loadContextRef == null)
            return true;

        var cts = new CancellationTokenSource(timeout);
        
        try
        {
            // More aggressive unloading approach
            while (!cts.Token.IsCancellationRequested)
            {
                // Multiple rounds of aggressive GC
                for (int round = 0; round < 3; round++)
                {
                    // Force full GC collection multiple times
                    for (int i = 0; i < 10; i++)
                    {
                        GC.Collect(GC.MaxGeneration);
                        GC.WaitForPendingFinalizers();
                    }
                    
                    // Check if unloaded after each round
                    if (!_loadContextRef.IsAlive)
                    {
                        _logger.LogInformation("Assembly load context successfully unloaded after round {Round}", round + 1);
                        return true;
                    }
                    
                    // Small delay between rounds
                    await Task.Delay(100, cts.Token);
                }

                // Wait longer between major attempts
                await Task.Delay(500, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when timeout occurs
        }

        _logger.LogWarning("Timeout waiting for plugin assembly context to unload");
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            _disposed = true;
            UnloadRequested = true;

            try
            {
                if (_plugin != null)
                {
                    try
                    {
                        // Request cancellation first
                        _plugin.RequestCancellation();
                        
                        var disposeTask = _plugin.DisposeAsync();
                        if (disposeTask.IsCompleted)
                        {
                            disposeTask.GetAwaiter().GetResult();
                        }
                        else
                        {
                            // Wait with timeout
                            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
                            var completedTask = Task.WhenAny(disposeTask.AsTask(), timeoutTask).GetAwaiter().GetResult();
                            
                            if (completedTask == timeoutTask)
                            {
                                _logger.LogWarning("Plugin disposal timed out after 10 seconds");
                            }
                            else
                            {
                                disposeTask.GetAwaiter().GetResult();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error disposing plugin");
                    }
                    finally
                    {
                        _plugin = null;
                    }
                }
                
                if (_loadContext != null)
                {
                    try
                    {
                        _logger.LogDebug("Starting aggressive AssemblyLoadContext unload");
                        
                        // Clear any potential references
                        _loadContext.Unload();
                        
                        // Clear strong reference immediately
                        _loadContext = null;
                        
                        // Force immediate garbage collection
                        for (int i = 0; i < 5; i++)
                        {
                            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                            GC.WaitForPendingFinalizers();
                        }
                        
                        _logger.LogDebug("AssemblyLoadContext unload initiated with aggressive cleanup");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error unloading AssemblyLoadContext");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during IsolatedPluginLoader disposal");
            }
        }
    }
}