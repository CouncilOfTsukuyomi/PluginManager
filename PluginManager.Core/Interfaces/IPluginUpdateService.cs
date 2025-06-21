using PluginManager.Core.Models;

namespace PluginManager.Core.Interfaces;

public interface IPluginUpdateService
{
    /// <summary>
    /// Checks for updates for plugins that have GitHub repository information in their metadata
    /// </summary>
    Task<List<PluginUpdateInfo>> CheckForUpdatesAsync(IEnumerable<DefaultPluginInfo> installedPlugins);

    /// <summary>
    /// Checks if a specific plugin has an update available
    /// </summary>
    Task<bool> IsUpdateAvailableAsync(DefaultPluginInfo plugin);

    /// <summary>
    /// Gets the latest version information for a plugin if an update is available
    /// </summary>
    Task<DefaultPluginInfo?> GetLatestVersionAsync(DefaultPluginInfo plugin);
}