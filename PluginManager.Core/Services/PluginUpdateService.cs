using Microsoft.Extensions.Logging;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;

namespace PluginManager.Core.Services;

public class PluginUpdateService : IPluginUpdateService
{
    private readonly IGitHubPluginProvider _gitHubProvider;
    private readonly IPluginDownloader _pluginDownloader;
    private readonly ILogger<PluginUpdateService> _logger;

    public PluginUpdateService(
        IGitHubPluginProvider gitHubProvider, 
        IPluginDownloader pluginDownloader,
        ILogger<PluginUpdateService> logger)
    {
        _gitHubProvider = gitHubProvider;
        _pluginDownloader = pluginDownloader;
        _logger = logger;
    }

    /// <summary>
    /// Checks for updates for plugins that have GitHub repository information in their metadata
    /// </summary>
    public async Task<List<PluginUpdateInfo>> CheckForUpdatesAsync(IEnumerable<DefaultPluginInfo> installedPlugins)
    {
        var updateInfos = new List<PluginUpdateInfo>();

        foreach (var plugin in installedPlugins)
        {
            try
            {
                var (owner, repo) = ExtractGitHubInfo(plugin);
                if (owner == null || repo == null)
                {
                    _logger.LogDebug("Plugin {PluginId} does not have GitHub repository information, skipping update check", plugin.Id);
                    continue;
                }

                _logger.LogDebug("Checking for updates for plugin {PluginId} from {Owner}/{Repo}", plugin.Id, owner, repo);

                var latestPlugin = await _gitHubProvider.CheckForUpdateAsync(plugin, owner, repo);
                if (latestPlugin != null)
                {
                    updateInfos.Add(new PluginUpdateInfo
                    {
                        CurrentPlugin = plugin,
                        LatestPlugin = latestPlugin,
                        UpdateAvailable = true
                    });
                    
                    _logger.LogInformation("Update available for {PluginId}: {CurrentVersion} → {LatestVersion}",
                        plugin.Id, plugin.Version, latestPlugin.Version);
                }
                else
                {
                    _logger.LogDebug("No update available for plugin {PluginId}", plugin.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for updates for plugin {PluginId}", plugin.Id);
            }
        }

        return updateInfos;
    }

    /// <summary>
    /// Checks if a specific plugin has an update available
    /// </summary>
    public async Task<bool> IsUpdateAvailableAsync(DefaultPluginInfo plugin)
    {
        try
        {
            var (owner, repo) = ExtractGitHubInfo(plugin);
            if (owner == null || repo == null)
            {
                _logger.LogDebug("Plugin {PluginId} does not have GitHub repository information", plugin.Id);
                return false;
            }

            return await _gitHubProvider.IsUpdateAvailableAsync(plugin, owner, repo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for update for plugin {PluginId}", plugin.Id);
            return false;
        }
    }

    /// <summary>
    /// Gets the latest version information for a plugin if an update is available
    /// </summary>
    public async Task<DefaultPluginInfo?> GetLatestVersionAsync(DefaultPluginInfo plugin)
    {
        try
        {
            var (owner, repo) = ExtractGitHubInfo(plugin);
            if (owner == null || repo == null)
            {
                _logger.LogDebug("Plugin {PluginId} does not have GitHub repository information", plugin.Id);
                return null;
            }

            return await _gitHubProvider.CheckForUpdateAsync(plugin, owner, repo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest version for plugin {PluginId}", plugin.Id);
            return null;
        }
    }

    /// <summary>
    /// Performs an update of a plugin to its latest version
    /// </summary>
    public async Task<PluginInstallResult> UpdatePluginAsync(DefaultPluginInfo currentPlugin, string? pluginsBasePath = null)
    {
        try
        {
            _logger.LogInformation("Starting update for plugin {PluginId}", currentPlugin.Id);

            var latestPlugin = await GetLatestVersionAsync(currentPlugin);
            if (latestPlugin == null)
            {
                return new PluginInstallResult
                {
                    Success = false,
                    ErrorMessage = "No update available or could not fetch latest version",
                    PluginId = currentPlugin.Id,
                    PluginName = currentPlugin.Name,
                    Version = currentPlugin.Version
                };
            }

            _logger.LogInformation("Updating plugin {PluginId} from {CurrentVersion} to {LatestVersion}",
                currentPlugin.Id, currentPlugin.Version, latestPlugin.Version);
            
            var installResult = await _pluginDownloader.DownloadAndInstallAsync(latestPlugin, pluginsBasePath);

            if (installResult.Success)
            {
                _logger.LogInformation("Successfully updated plugin {PluginId} to version {Version}",
                    currentPlugin.Id, latestPlugin.Version);
            }
            else
            {
                _logger.LogError("Failed to update plugin {PluginId}: {Error}",
                    currentPlugin.Id, installResult.ErrorMessage);
            }

            return installResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating plugin {PluginId}", currentPlugin.Id);
            return new PluginInstallResult
            {
                Success = false,
                ErrorMessage = $"Update failed: {ex.Message}",
                PluginId = currentPlugin.Id,
                PluginName = currentPlugin.Name,
                Version = currentPlugin.Version
            };
        }
    }

    private (string? owner, string? repo) ExtractGitHubInfo(DefaultPluginInfo plugin)
    {
        // Try to extract GitHub repository information from metadata
        if (plugin.Metadata?.TryGetValue("githubRepo", out var repoValue) == true && 
            repoValue is string repoStr && !string.IsNullOrEmpty(repoStr))
        {
            var parts = repoStr.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                return (parts[0], parts[1]);
            }
        }

        // Alternative: check for separate owner/repo metadata keys
        object? ownerValue = null;
        object? repoValue2 = null;
        
        var hasOwner = plugin.Metadata?.TryGetValue("githubOwner", out ownerValue) == true;
        var hasRepo = plugin.Metadata?.TryGetValue("githubRepository", out repoValue2) == true;
        
        if (hasOwner && hasRepo && 
            ownerValue is string owner && repoValue2 is string repo &&
            !string.IsNullOrEmpty(owner) && !string.IsNullOrEmpty(repo))
        {
            return (owner, repo);
        }

        return (null, null);
    }
}