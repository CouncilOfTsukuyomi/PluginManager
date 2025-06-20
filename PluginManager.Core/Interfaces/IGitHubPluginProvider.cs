using PluginManager.Core.Models;

namespace PluginManager.Core.Interfaces;

public interface IGitHubPluginProvider
{
    /// <summary>
    /// Gets the latest plugin release from a GitHub repository
    /// </summary>
    Task<DefaultPluginInfo?> GetLatestPluginAsync(string owner, string repo, string pluginId, string pluginName);
    
    /// <summary>
    /// Gets all releases for a GitHub repository
    /// </summary>
    Task<IEnumerable<DefaultPluginInfo>> GetAllReleasesAsync(string owner, string repo, string pluginId, string pluginName);
    
    /// <summary>
    /// Gets a specific release by tag
    /// </summary>
    Task<DefaultPluginInfo?> GetReleaseByTagAsync(string owner, string repo, string tag, string pluginId, string pluginName);
}