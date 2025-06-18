using PluginManager.Core.Models;

namespace PluginManager.Core.Interfaces;

public interface IPluginService
{
    /// <summary>
    /// Get all registered plugins
    /// </summary>
    IReadOnlyList<IModPlugin> GetAllPlugins();

    /// <summary>
    /// Get all enabled plugins
    /// </summary>
    IReadOnlyList<IModPlugin> GetEnabledPlugins();

    /// <summary>
    /// Get a specific plugin by ID
    /// </summary>
    IModPlugin? GetPlugin(string pluginId);

    /// <summary>
    /// Register a new plugin
    /// </summary>
    Task RegisterPluginAsync(IModPlugin plugin);

    /// <summary>
    /// Unregister a plugin
    /// </summary>
    Task UnregisterPluginAsync(string pluginId);

    /// <summary>
    /// Get recent mods from all enabled plugins
    /// </summary>
    Task<List<PluginMod>> GetAllRecentModsAsync();

    /// <summary>
    /// Get recent mods from a specific plugin
    /// </summary>
    Task<List<PluginMod>> GetRecentModsFromPluginAsync(string pluginId);
}