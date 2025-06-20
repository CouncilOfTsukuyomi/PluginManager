using PluginManager.Core.Models;

namespace PluginManager.Core.Interfaces;

public interface IPluginDownloader
{
    /// <summary>
    /// Downloads a plugin from the specified URL
    /// </summary>
    Task<PluginDownloadResult> DownloadAsync(string downloadUrl, string? fileName = null);

    /// <summary>
    /// Downloads a plugin by its info object
    /// </summary>
    Task<PluginDownloadResult> DownloadAsync(DefaultPluginInfo pluginInfo);
}