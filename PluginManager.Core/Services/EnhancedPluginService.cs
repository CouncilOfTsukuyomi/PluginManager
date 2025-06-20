using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;
using PluginManager.Core.Security;

namespace PluginManager.Core.Services;

public class EnhancedPluginService : IPluginService, IPluginManagementService, IDisposable
{
    private readonly ILogger<EnhancedPluginService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IPluginDiscoveryService _discoveryService;
    private readonly ConcurrentDictionary<string, IModPlugin> _loadedPlugins = new();
    private readonly ConcurrentDictionary<string, IDisposable> _pluginLoaders = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    public EnhancedPluginService(
        ILogger<EnhancedPluginService> logger,
        ILoggerFactory loggerFactory,
        IPluginDiscoveryService discoveryService)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _discoveryService = discoveryService;
    }
    
    public async Task<List<PluginInfo>> GetAvailablePluginsAsync()
    {
        var pluginInfos = await _discoveryService.GetAllPluginInfoAsync();
    
        // Update IsLoaded based on what's actually loaded in this service
        foreach (var pluginInfo in pluginInfos)
        {
            pluginInfo.IsLoaded = _loadedPlugins.ContainsKey(pluginInfo.PluginId);
        }
    
        return pluginInfos;
    }
    
    

    public async Task SetPluginEnabledAsync(string pluginId, bool enabled)
    {
        await _discoveryService.SetPluginEnabledAsync(pluginId, enabled);

        if (enabled && !_loadedPlugins.ContainsKey(pluginId))
        {
            // Load the plugin
            var pluginInfos = await _discoveryService.GetAllPluginInfoAsync();
            var pluginInfo = pluginInfos.FirstOrDefault(p => p.PluginId == pluginId);
        
            if (pluginInfo != null)
            {
                await LoadPluginSecurelyAsync(pluginInfo);
            }
        }
        else if (!enabled && _loadedPlugins.ContainsKey(pluginId))
        {
            // Unload the plugin
            await UnregisterPluginAsync(pluginId);
        }
    }

    public async Task UpdatePluginConfigurationAsync(string pluginId, Dictionary<string, object> configuration)
    {
        await _discoveryService.UpdatePluginConfigurationAsync(pluginId, configuration);

        // If plugin is loaded, reinitialize it with new config
        if (_loadedPlugins.TryGetValue(pluginId, out var plugin))
        {
            try
            {
                await plugin.InitializeAsync(configuration);
                _logger.LogInformation("Plugin {PluginId} reconfigured successfully", pluginId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reconfigure plugin {PluginId}", pluginId);
                throw;
            }
        }
    }

    public async Task<PluginSettings> GetPluginSettingsAsync(string pluginDirectory)
    {
        return await _discoveryService.GetPluginSettingsAsync(pluginDirectory);
    }

    public async Task SavePluginSettingsAsync(string pluginDirectory, PluginSettings settings)
    {
        await _discoveryService.SavePluginSettingsAsync(pluginDirectory, settings);
    }

    /// <summary>
    /// Initialise and load all enabled plugins with multi-layer security
    /// </summary>
    public async Task InitializeAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            var pluginInfos = await _discoveryService.GetAllPluginInfoAsync();
            var enabledPlugins = pluginInfos.Where(p => p.IsEnabled).ToList();

            _logger.LogInformation("Loading {Count} enabled plugins with multi-layer security", enabledPlugins.Count);

            foreach (var pluginInfo in enabledPlugins)
            {
                try
                {
                    await LoadPluginSecurelyAsync(pluginInfo);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load plugin {PluginId}", pluginInfo.PluginId);
                    pluginInfo.LoadError = ex.Message;
                }
            }

            _logger.LogInformation("Successfully loaded {Count} secure plugins", _loadedPlugins.Count);
        }
        finally
        {
            _semaphore.Release();
        }
    }


    private async Task LoadPluginSecurelyAsync(PluginInfo pluginInfo)
    {
        try
        {
            _logger.LogDebug("Loading plugin {PluginId} with multi-layer security", pluginInfo.PluginId);
            
            // Layer 1: Choose isolation method based on platform and configuration
            IModPlugin? plugin = null;
            IDisposable? loader = null;

            // Try AppDomain-based sandboxing first (more secure but .NET Framework only)
            if (await TryCreateDomainLoaderAsync(pluginInfo) is var domainResult && domainResult.Success)
            {
                plugin = domainResult.Plugin;
                loader = domainResult.Loader;
                _logger.LogDebug("Loaded plugin {PluginId} using AppDomain sandbox", pluginInfo.PluginId);
            }
            // Fallback to AssemblyLoadContext isolation (.NET Core/5+)
            else if (await TryCreateIsolatedLoaderAsync(pluginInfo) is var isolatedResult && isolatedResult.Success)
            {
                plugin = isolatedResult.Plugin;
                loader = isolatedResult.Loader;
                _logger.LogDebug("Loaded plugin {PluginId} using AssemblyLoadContext isolation", pluginInfo.PluginId);
            }
            else
            {
                throw new Exception("Failed to load plugin with any isolation method");
            }

            if (plugin != null)
            {
                // Layer 2: Wrap in security proxy for runtime protection
                var securePlugin = new SecurityPluginProxy(plugin, _logger);
                
                // Layer 3: Initialize with validated configuration
                var sanitizedConfig = SanitizeConfiguration(pluginInfo.Configuration);
                await securePlugin.InitializeAsync(sanitizedConfig);
                
                // Store both the secure plugin and the loader
                _loadedPlugins[plugin.PluginId] = securePlugin;
                if (loader != null)
                {
                    _pluginLoaders[plugin.PluginId] = loader;
                }
                
                // Don't set IsLoaded here - it should be set by GetAvailablePluginsAsync()
                // which checks the actual _loadedPlugins dictionary
                
                _logger.LogInformation("Successfully loaded multi-layered secure plugin: {PluginId}", plugin.PluginId);
            }
            else
            {
                loader?.Dispose();
                throw new Exception("Failed to create plugin instance");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load plugin {PluginId} securely", pluginInfo.PluginId);
            throw;
        }
    }

    private async Task<(bool Success, IModPlugin? Plugin, IDisposable? Loader)> TryCreateDomainLoaderAsync(PluginInfo pluginInfo)
    {
        try
        {
            _logger.LogDebug("Attempting AppDomain loading for plugin {PluginId}", pluginInfo.PluginId);
            
            // Create AppDomain-based sandbox loader
            var domainLoader = new PluginDomainLoader();
            var plugin = domainLoader.LoadPlugin(pluginInfo.AssemblyPath, pluginInfo.TypeName, pluginInfo.PluginDirectory);
            
            if (plugin != null)
            {
                return (true, plugin, domainLoader);
            }
            
            domainLoader.Dispose();
            return (false, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AppDomain loading failed for plugin {PluginId}, trying AssemblyLoadContext", pluginInfo.PluginId);
            return (false, null, null);
        }
    }

    private async Task<(bool Success, IModPlugin? Plugin, IDisposable? Loader)> TryCreateIsolatedLoaderAsync(PluginInfo pluginInfo)
    {
        try
        {
            _logger.LogDebug("Attempting AssemblyLoadContext loading for plugin {PluginId}", pluginInfo.PluginId);
            
            // Create AssemblyLoadContext-based isolation
            var loaderLogger = _loggerFactory?.CreateLogger<IsolatedPluginLoader>() ?? NullLogger<IsolatedPluginLoader>.Instance;
            var isolatedLoader = new IsolatedPluginLoader(loaderLogger, pluginInfo.PluginDirectory);
            
            // PROPERLY await the async operation instead of using GetAwaiter().GetResult()
            var plugin = await isolatedLoader.LoadPluginAsync(pluginInfo.AssemblyPath, pluginInfo.TypeName);
            
            if (plugin != null)
            {
                return (true, plugin, isolatedLoader);
            }
            
            isolatedLoader.Dispose();
            return (false, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AssemblyLoadContext loading failed for plugin {PluginId}: {Message}", pluginInfo.PluginId, ex.Message);
            return (false, null, null);
        }
    }

    private Dictionary<string, object> SanitizeConfiguration(Dictionary<string, object> configuration)
    {
        var sanitized = new Dictionary<string, object>();
        var policy = SecurityPolicy.Default;

        foreach (var kvp in configuration)
        {
            if (policy.AllowAllConfigKeys || policy.AllowedConfigKeys.Contains(kvp.Key))
            {
                // Basic sanitization of values
                var sanitizedValue = SanitizeConfigValue(kvp.Value);
                if (sanitizedValue != null)
                {
                    sanitized[kvp.Key] = sanitizedValue;
                }
            }
            else
            {
                _logger.LogDebug("Configuration key {Key} filtered out by security policy", kvp.Key);
            }
        }

        return sanitized;
    }

    private object? SanitizeConfigValue(object value)
    {
        if (value is string stringValue)
        {
            // Remove dangerous content from strings
            var sanitized = stringValue
                .Replace("javascript:", "", StringComparison.OrdinalIgnoreCase)
                .Replace("file://", "", StringComparison.OrdinalIgnoreCase)
                .Replace("<script", "", StringComparison.OrdinalIgnoreCase);

            return sanitized.Length <= SecurityPolicy.Default.MaxStringLength 
                ? sanitized 
                : sanitized[..SecurityPolicy.Default.MaxStringLength];
        }

        return value;
    }
    
    public IReadOnlyList<IModPlugin> GetAllPlugins()
    {
        return _loadedPlugins.Values.ToList();
    }

    public IReadOnlyList<IModPlugin> GetEnabledPlugins()
    {
        return _loadedPlugins.Values.Where(p => p.IsEnabled).ToList();
    }

    public IModPlugin? GetPlugin(string pluginId)
    {
        _loadedPlugins.TryGetValue(pluginId, out var plugin);
        return plugin;
    }

    public async Task RegisterPluginAsync(IModPlugin plugin)
    {
        if (plugin == null)
            throw new ArgumentNullException(nameof(plugin));

        await _semaphore.WaitAsync();
        try
        {
            if (_loadedPlugins.ContainsKey(plugin.PluginId))
            {
                _logger.LogWarning("Plugin {PluginId} is already registered", plugin.PluginId);
                return;
            }

            // Wrap in security proxy if not already wrapped
            var securePlugin = plugin is SecurityPluginProxy ? plugin : new SecurityPluginProxy(plugin, _logger);
            
            _loadedPlugins[plugin.PluginId] = securePlugin;
            _logger.LogInformation("Registered secure plugin: {PluginId} - {DisplayName}", 
                plugin.PluginId, plugin.DisplayName);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task UnregisterPluginAsync(string pluginId)
    {
        await _semaphore.WaitAsync();
        try
        {
            if (_loadedPlugins.TryRemove(pluginId, out var plugin))
            {
                plugin.IsEnabled = false;
                
                try
                {
                    await plugin.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing plugin {PluginId}", pluginId);
                }
                
                _logger.LogInformation("Unregistered plugin: {PluginId}", pluginId);
            }

            // Dispose the plugin loader
            if (_pluginLoaders.TryRemove(pluginId, out var loader))
            {
                try
                {
                    loader.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing plugin loader for {PluginId}", pluginId);
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<PluginMod>> GetAllRecentModsAsync()
    {
        var enabledPlugins = GetEnabledPlugins();
        var allMods = new List<PluginMod>();

        var tasks = enabledPlugins.Select(async plugin =>
        {
            try
            {
                var mods = await plugin.GetRecentModsAsync();
                return mods.Select(mod =>
                {
                    mod.PluginSource = plugin.PluginId;
                    return mod;
                }).ToList();
            }
            catch (SecurityException ex)
            {
                _logger.LogWarning("Security violation in plugin {PluginId}: {Message}", plugin.PluginId, ex.Message);
                return new List<PluginMod>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get recent mods from plugin {PluginId}", plugin.PluginId);
                return new List<PluginMod>();
            }
        });

        var results = await Task.WhenAll(tasks);
        
        foreach (var modList in results)
        {
            allMods.AddRange(modList);
        }

        return allMods
            .GroupBy(m => m.ModUrl)
            .Select(g => g.First())
            .ToList();
    }

    public async Task<List<PluginMod>> GetRecentModsFromPluginAsync(string pluginId)
    {
        var plugin = GetPlugin(pluginId);
        if (plugin == null || !plugin.IsEnabled)
        {
            _logger.LogWarning("Plugin {PluginId} not found or not enabled", pluginId);
            return new List<PluginMod>();
        }

        try
        {
            var mods = await plugin.GetRecentModsAsync();
            return mods.Select(mod =>
            {
                mod.PluginSource = plugin.PluginId;
                return mod;
            }).ToList();
        }
        catch (SecurityException ex)
        {
            _logger.LogWarning("Security violation in plugin {PluginId}: {Message}", pluginId, ex.Message);
            return new List<PluginMod>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recent mods from plugin {PluginId}", pluginId);
            return new List<PluginMod>();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        // Dispose all plugins
        var disposeTasks = _loadedPlugins.Values.Select(async plugin =>
        {
            try
            {
                await plugin.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing plugin {PluginId}", plugin.PluginId);
            }
        }).ToArray();

        try
        {
            Task.WaitAll(disposeTasks, TimeSpan.FromSeconds(30));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error waiting for plugin disposal tasks to complete");
        }

        // Dispose all plugin loaders
        foreach (var loader in _pluginLoaders.Values)
        {
            try
            {
                loader.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing plugin loader");
            }
        }

        _semaphore.Dispose();
        _disposed = true;
    }
}