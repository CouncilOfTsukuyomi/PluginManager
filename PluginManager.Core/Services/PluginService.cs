using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;

namespace PluginManager.Core.Services;

public class PluginService : IPluginService, IDisposable
{
    private readonly ILogger<PluginService> _logger;
    private readonly ConcurrentDictionary<string, IModPlugin> _plugins = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    public PluginService(ILogger<PluginService> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<IModPlugin> GetAllPlugins()
    {
        return _plugins.Values.ToList();
    }

    public IReadOnlyList<IModPlugin> GetEnabledPlugins()
    {
        return _plugins.Values.Where(p => p.IsEnabled).ToList();
    }

    public IModPlugin? GetPlugin(string pluginId)
    {
        _plugins.TryGetValue(pluginId, out var plugin);
        return plugin;
    }

    public async Task RegisterPluginAsync(IModPlugin plugin)
    {
        if (plugin == null)
            throw new ArgumentNullException(nameof(plugin));

        await _semaphore.WaitAsync();
        try
        {
            if (_plugins.ContainsKey(plugin.PluginId))
            {
                _logger.LogWarning("Plugin {PluginId} is already registered", plugin.PluginId);
                return;
            }

            _plugins[plugin.PluginId] = plugin;
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
            if (_plugins.TryRemove(pluginId, out var plugin))
            {
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

        // Remove duplicates based on ModUrl
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
        var disposeTasks = _plugins.Values.Select(async plugin =>
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