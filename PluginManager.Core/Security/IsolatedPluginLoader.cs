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
                
                _loadContext = new PluginLoadContext(assemblyPath, _logger);
                _loadContextRef = new WeakReference(_loadContext, trackResurrection: true);

                // Load the plugin assembly into the isolated context
                var assembly = _loadContext.LoadFromAssemblyPath(assemblyPath);
                var pluginType = assembly.GetType(typeName);

                if (pluginType == null)
                {
                    throw new InvalidOperationException($"Type {typeName} not found in assembly {assemblyPath}");
                }

                if (!typeof(IModPlugin).IsAssignableFrom(pluginType))
                {
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

    private IModPlugin? CreatePluginInstance(Type pluginType)
    {
        var constructors = pluginType.GetConstructors()
            .OrderBy(c => c.GetParameters().Length);

        foreach (var constructor in constructors)
        {
            try
            {
                var parameters = constructor.GetParameters();
                var args = new object[parameters.Length];

                for (int i = 0; i < parameters.Length; i++)
                {
                    var paramType = parameters[i].ParameterType;
                    args[i] = CreateParameterValue(paramType, parameters[i]);
                }

                var instance = Activator.CreateInstance(pluginType, args) as IModPlugin;
                if (instance != null)
                {
                    return instance;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to create plugin instance with constructor {Constructor}", constructor);
                continue;
            }
        }

        return null;
    }

    private object? CreateParameterValue(Type paramType, ParameterInfo parameter)
    {
        // Provide minimal dependencies for plugin construction
        if (paramType.Name.Contains("ILogger") || 
            (paramType.IsGenericType && paramType.GetGenericTypeDefinition().Name.Contains("ILogger")))
        {
            return new NullLoggerAdapter();
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
        if (paramType.IsGenericType && paramType.GetGenericTypeDefinition() == typeof(Nullable<>))
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
    
        return parameter.HasDefaultValue ? parameter.DefaultValue : null;
    }

    public async Task<bool> WaitForUnloadAsync(TimeSpan timeout)
    {
        if (_loadContextRef == null)
            return true;

        var cts = new CancellationTokenSource(timeout);
        
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                // Force aggressive garbage collection
                for (int i = 0; i < 5; i++)
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
                }

                if (!_loadContextRef.IsAlive)
                {
                    _logger.LogInformation("Assembly load context successfully unloaded");
                    return true;
                }

                await Task.Delay(200, cts.Token);
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
                // Dispose plugin first
                if (_plugin != null)
                {
                    try
                    {
                        var disposeTask = _plugin.DisposeAsync();
                        if (disposeTask.IsCompleted)
                        {
                            disposeTask.GetAwaiter().GetResult();
                        }
                        else
                        {
                            // Don't wait too long for plugin disposal - simple fire and forget
                            ThreadPool.QueueUserWorkItem(async _ =>
                            {
                                try
                                {
                                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                                    
                                    // Convert ValueTask to Task and handle timeout
                                    var task = disposeTask.AsTask();
                                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
                                    
                                    var completedTask = await Task.WhenAny(task, timeoutTask);
                                    if (completedTask == timeoutTask)
                                    {
                                        _logger.LogWarning("Plugin disposal timed out after 5 seconds");
                                    }
                                    else
                                    {
                                        // Ensure we await the actual dispose task to get any exceptions
                                        await task;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Plugin disposal failed or timed out");
                                }
                            });
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

                // Unload the AssemblyLoadContext
                if (_loadContext != null)
                {
                    try
                    {
                        _logger.LogDebug("Starting AssemblyLoadContext unload");
                        _loadContext.Unload();
                        
                        // Clear strong reference immediately
                        _loadContext = null;
                        
                        _logger.LogDebug("AssemblyLoadContext unload initiated");
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

// Simple logger adapter to avoid complex dependencies
internal class NullLoggerAdapter : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}