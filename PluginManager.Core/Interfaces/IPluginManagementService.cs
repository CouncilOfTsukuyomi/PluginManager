using PluginManager.Core.Models;

namespace PluginManager.Core.Interfaces;

public interface IPluginManagementService
{
    /// <summary>
    /// Get all available plugins (loaded and discovered)
    /// </summary>
    Task<List<PluginInfo>> GetAvailablePluginsAsync();

    /// <summary>
    /// Enable or disable a plugin
    /// </summary>
    Task SetPluginEnabledAsync(string pluginId, bool enabled);

    /// <summary>
    /// Update plugin configuration
    /// </summary>
    Task UpdatePluginConfigurationAsync(string pluginId, Dictionary<string, object> configuration);

    /// <summary>
    /// Initialize all enabled plugins
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Get plugin settings for a specific plugin
    /// </summary>
    Task<PluginSettings> GetPluginSettingsAsync(string pluginDirectory);

    /// <summary>
    /// Save plugin settings for a specific plugin
    /// </summary>
    Task SavePluginSettingsAsync(string pluginDirectory, PluginSettings settings);

}