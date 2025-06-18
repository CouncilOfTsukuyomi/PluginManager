using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;

namespace PluginManager.Core.Services;

/// <summary>
/// Enhanced plugin service with discovery and user management
/// </summary>
public class EnhancedPluginService : IPluginService, IDisposable
{
    private readonly ILogger<EnhancedPluginService> _logger;
    private readonly IPluginDiscoveryService _discoveryService;
    private readonly ConcurrentDictionary<string, IModPlugin> _loadedPlugins = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    public EnhancedPluginService(
        ILogger<EnhancedPluginService> logger,
        IPluginDiscoveryService discoveryService)
    {
        _logger = logger;
        _discoveryService = discoveryService;
    }

    /// <summary>
    /// Initialize and load all enabled plugins
    /// </summary>
    public async Task InitializeAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            var pluginInfos = await _discoveryService.GetAllPluginInfoAsync();
            var enabledPlugins = pluginInfos.Where(p => p.IsEnabled).ToList();

            _logger.LogInformation("Loading {Count} enabled plugins", enabledPlugins.Count);

            foreach (var pluginInfo in enabledPlugins)
            {
                try
                {
                    var plugin = await _discoveryService.LoadPluginAsync(pluginInfo);
                    if (plugin != null)
                    {
                        _loadedPlugins[plugin.PluginId] = plugin;
                        pluginInfo.IsLoaded = true;
                        pluginInfo.LoadError = null;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load plugin {PluginId}", pluginInfo.PluginId);
                    pluginInfo.LoadError = ex.Message;
                }
            }

            _logger.LogInformation("Successfully loaded {Count} plugins", _loadedPlugins.Count);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Get all available plugins (loaded and discovered)
    /// </summary>
    public async Task<List<PluginInfo>> GetAvailablePluginsAsync()
    {
        return await _discoveryService.GetAllPluginInfoAsync();
    }

    /// <summary>
    /// Enable or disable a plugin
    /// </summary>
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
                var plugin = await _discoveryService.LoadPluginAsync(pluginInfo);
                if (plugin != null)
                {
                    await RegisterPluginAsync(plugin);
                }
            }
        }
        else if (!enabled && _loadedPlugins.ContainsKey(pluginId))
        {
            // Unload the plugin
            await UnregisterPluginAsync(pluginId);
        }
    }

    /// <summary>
    /// Update plugin configuration
    /// </summary>
    public async Task UpdatePluginConfigurationAsync(string pluginId, Dictionary<string, object> configuration)
    {
        await _discoveryService.UpdatePluginConfigurationAsync(pluginId, configuration);

        // If plugin is loaded, reinitialize it with new config
        if (_loadedPlugins.TryGetValue(pluginId, out var plugin))
        {
            await plugin.InitializeAsync(configuration);
        }
    }

    // IPluginService implementation
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

            _loadedPlugins[plugin.PluginId] = plugin;
            _logger.LogInformation("Registered plugin: {PluginId} - {DisplayName}", 
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
                await plugin.DisposeAsync();
                _logger.LogInformation("Unregistered plugin: {PluginId}", pluginId);
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

        // Convert ValueTask to Task and handle disposal
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

        _semaphore.Dispose();
        _disposed = true;
    }
}