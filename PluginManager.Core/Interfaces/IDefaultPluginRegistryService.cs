using PluginManager.Core.Models;

namespace PluginManager.Core.Interfaces;

public interface IDefaultPluginRegistryService
{
    /// <summary>
    /// Fetches the default plugin registry from the configured URL
    /// </summary>
    Task<DefaultPluginRegistry> GetRegistryAsync();

    /// <summary>
    /// Gets all available default plugins
    /// </summary>
    Task<IEnumerable<DefaultPluginInfo>> GetAvailablePluginsAsync();

    /// <summary>
    /// Gets a specific plugin by ID
    /// </summary>
    Task<DefaultPluginInfo?> GetPluginAsync(string pluginId);

    /// <summary>
    /// Searches for plugins by name or description
    /// </summary>
    Task<IEnumerable<DefaultPluginInfo>> SearchPluginsAsync(string searchTerm);

    /// <summary>
    /// Gets plugins by category
    /// </summary>
    Task<IEnumerable<DefaultPluginInfo>> GetPluginsByCategoryAsync(string category);

    /// <summary>
    /// Clears the cached registry, forcing a fresh fetch on next request
    /// </summary>
    void ClearCache();
}