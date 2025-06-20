using PluginManager.Core.Models;

namespace PluginManager.Core.Interfaces;

public interface IGitHubPluginProvider
{
    Task<DefaultPluginInfo?> GetLatestPluginAsync(string owner, string repo, string pluginId, string pluginName, string? assetNamePattern = null);
    Task<IEnumerable<DefaultPluginInfo>> GetAllReleasesAsync(string owner, string repo, string pluginId, string pluginName);
    Task<DefaultPluginInfo?> GetReleaseByTagAsync(string owner, string repo, string tag, string pluginId, string pluginName);
}