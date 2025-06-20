using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;
using PluginManager.Core.Models.GitHub;

namespace PluginManager.Core.Services;

public class GitHubPluginProvider : IGitHubPluginProvider
{
    private readonly ILogger<GitHubPluginProvider> _logger;
    private readonly HttpClient _httpClient;

    public GitHubPluginProvider(ILogger<GitHubPluginProvider> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        
        // Set user agent for GitHub API
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PluginManager/1.0");
        }
    }

    public async Task<DefaultPluginInfo?> GetLatestPluginAsync(string owner, string repo, string pluginId, string pluginName, string? assetNamePattern = null)
    {
        try
        {
            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            _logger.LogDebug("Fetching latest release from {Url}", apiUrl);
            
            var response = await _httpClient.GetStringAsync(apiUrl);
            var release = JsonSerializer.Deserialize<GitHubRelease>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (release == null)
            {
                _logger.LogWarning("Failed to deserialize GitHub release for {Owner}/{Repo}", owner, repo);
                return null;
            }

            return ConvertToPluginInfo(release, owner, repo, pluginId, pluginName, assetNamePattern);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching latest release for {Owner}/{Repo}", owner, repo);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch latest release for {Owner}/{Repo}", owner, repo);
            return null;
        }
    }

    public async Task<IEnumerable<DefaultPluginInfo>> GetAllReleasesAsync(string owner, string repo, string pluginId, string pluginName)
    {
        try
        {
            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases";
            _logger.LogDebug("Fetching all releases from {Url}", apiUrl);
            
            var response = await _httpClient.GetStringAsync(apiUrl);
            var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (releases == null)
            {
                _logger.LogWarning("Failed to deserialize GitHub releases for {Owner}/{Repo}", owner, repo);
                return Enumerable.Empty<DefaultPluginInfo>();
            }

            return releases
                .Where(r => !r.Prerelease) // Skip prereleases by default
                .Select(r => ConvertToPluginInfo(r, owner, repo, pluginId, pluginName))
                .Where(p => p != null)
                .Cast<DefaultPluginInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch all releases for {Owner}/{Repo}", owner, repo);
            return Enumerable.Empty<DefaultPluginInfo>();
        }
    }

    public async Task<DefaultPluginInfo?> GetReleaseByTagAsync(string owner, string repo, string tag, string pluginId, string pluginName)
    {
        try
        {
            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/tags/{tag}";
            _logger.LogDebug("Fetching release {Tag} from {Url}", tag, apiUrl);
            
            var response = await _httpClient.GetStringAsync(apiUrl);
            var release = JsonSerializer.Deserialize<GitHubRelease>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (release == null)
            {
                _logger.LogWarning("Failed to deserialize GitHub release {Tag} for {Owner}/{Repo}", tag, owner, repo);
                return null;
            }

            return ConvertToPluginInfo(release, owner, repo, pluginId, pluginName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch release {Tag} for {Owner}/{Repo}", tag, owner, repo);
            return null;
        }
    }

    private DefaultPluginInfo? ConvertToPluginInfo(GitHubRelease release, string owner, string repo, string pluginId, string pluginName, string? assetNamePattern = null)
    {
        // Find the appropriate asset
        var asset = FindMatchingAsset(release.Assets, assetNamePattern);
        if (asset == null)
        {
            _logger.LogWarning("No matching asset found in release {Tag} for {Owner}/{Repo}", release.TagName, owner, repo);
            return null;
        }

        return new DefaultPluginInfo
        {
            Id = pluginId,
            Name = pluginName,
            Description = !string.IsNullOrEmpty(release.Body) ? release.Body : "No description available",
            Version = release.TagName,
            Author = owner,
            Category = "GitHub",
            Tags = new List<string> { "github", "community", owner.ToLowerInvariant() },
            DownloadUrl = asset.BrowserDownloadUrl,
            SizeInBytes = asset.Size,
            LastUpdated = release.PublishedAt,
            Metadata = new Dictionary<string, object>
            {
                ["githubRepo"] = $"{owner}/{repo}",
                ["releaseUrl"] = release.HtmlUrl,
                ["prerelease"] = release.Prerelease,
                ["assetName"] = asset.Name,
                ["downloadCount"] = asset.DownloadCount
            }
        };
    }

    private GitHubAsset? FindMatchingAsset(List<GitHubAsset>? assets, string? assetNamePattern = null)
    {
        if (assets == null || !assets.Any())
        {
            _logger.LogWarning("No assets found in release");
            return null;
        }

        _logger.LogDebug("Available assets: {Assets}", string.Join(", ", assets.Select(a => a.Name)));
        _logger.LogDebug("Asset name pattern: {Pattern}", assetNamePattern ?? "none (defaulting to .zip)");

        // If no pattern specified, default to first .zip file
        if (string.IsNullOrEmpty(assetNamePattern))
        {
            var zipAsset = assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (zipAsset != null)
            {
                _logger.LogDebug("Selected asset: {AssetName} (default .zip match)", zipAsset.Name);
            }
            return zipAsset;
        }

        // Use regex pattern matching
        try
        {
            var regex = new Regex(assetNamePattern, RegexOptions.IgnoreCase);
            var matchedAsset = assets.FirstOrDefault(a => regex.IsMatch(a.Name));
            if (matchedAsset != null)
            {
                _logger.LogDebug("Selected asset: {AssetName} (regex match)", matchedAsset.Name);
            }
            else
            {
                _logger.LogWarning("No asset matched pattern {Pattern}", assetNamePattern);
            }
            return matchedAsset;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Regex pattern {Pattern} failed, falling back to contains check", assetNamePattern);
            // If regex fails, fall back to simple contains check
            var containsAsset = assets.FirstOrDefault(a => a.Name.Contains(assetNamePattern, StringComparison.OrdinalIgnoreCase));
            if (containsAsset != null)
            {
                _logger.LogDebug("Selected asset: {AssetName} (contains match)", containsAsset.Name);
            }
            return containsAsset;
        }
    }
}