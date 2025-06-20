using PluginManager.Core.Models;

namespace PluginManager.Core.Interfaces;

public interface IPluginDiscoveryService
{
    /// <summary>
    /// Scan the plugins directory for available plugins
    /// </summary>
    Task<List<PluginInfo>> DiscoverPluginsAsync();

    /// <summary>
    /// Load a specific plugin instance
    /// </summary>
    Task<IModPlugin?> LoadPluginAsync(PluginInfo pluginInfo);

    /// <summary>
    /// Get all discovered plugins with their current status
    /// </summary>
    Task<List<PluginInfo>> GetAllPluginInfoAsync();

    /// <summary>
    /// Enable or disable a plugin
    /// </summary>
    Task SetPluginEnabledAsync(string pluginId, bool enabled);

    /// <summary>
    /// Update plugin configuration
    /// </summary>
    Task UpdatePluginConfigurationAsync(string pluginId, Dictionary<string, object> configuration);

    /// <summary>
    /// Get plugin settings from its directory
    /// </summary>
    Task<PluginSettings> GetPluginSettingsAsync(string pluginDirectory);

    /// <summary>
    /// Save plugin settings to its directory
    /// </summary>
    Task SavePluginSettingsAsync(string pluginDirectory, PluginSettings settings);
    
    /// <summary>
    /// Checks if a plugin has configurable settings defined in its schema
    /// </summary>
    Task<bool> HasConfigurableSettingsAsync(string pluginDirectory);

}