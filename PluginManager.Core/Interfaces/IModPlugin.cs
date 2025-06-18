using PluginManager.Core.Models;

namespace PluginManager.Core.Interfaces;

/// <summary>
/// Interface that all mod plugins must implement
/// </summary>
public interface IModPlugin
{
    /// <summary>
    /// Unique identifier for this plugin
    /// </summary>
    string PluginId { get; }

    /// <summary>
    /// Human-readable name for this plugin
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Description of what this plugin provides
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Version of this plugin
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Author of this plugin
    /// </summary>
    string Author { get; }

    /// <summary>
    /// Whether this plugin is currently enabled
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Directory where this plugin's files are stored
    /// </summary>
    string PluginDirectory { get; set; }

    /// <summary>
    /// Get recent mods from this plugin source with download links included
    /// </summary>
    Task<List<PluginMod>> GetRecentModsAsync();

    /// <summary>
    /// Initialize the plugin with configuration
    /// </summary>
    Task InitializeAsync(Dictionary<string, object> configuration);

    /// <summary>
    /// Cleanup resources when the plugin is being disposed
    /// </summary>
    ValueTask DisposeAsync();
}