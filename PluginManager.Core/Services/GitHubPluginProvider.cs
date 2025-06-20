
using System.Net.Http.Headers;
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
        
        // Set user agent for GitHub API (similar to your working code)
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("PluginManager", "1.0"));
        }
    }

    public async Task<DefaultPluginInfo?> GetLatestPluginAsync(string owner, string repo, string pluginId, string pluginName, string? assetNamePattern = null)
    {
        try
        {
            // Use /releases endpoint like your working code, not /releases/latest
            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases";
            _logger.LogDebug("Fetching releases from {Url}", apiUrl);
            
            using var response = await _httpClient.GetAsync(apiUrl);
            _logger.LogDebug("GitHub releases GET request completed with status code {StatusCode}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Request for releases did not succeed with status {StatusCode}. Response: {ErrorContent}",
                    response.StatusCode, errorContent);
                return null;
            }

            List<GitHubRelease>? releases;
            try
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("GitHub API response length: {Length}", responseContent.Length);
                
                releases = JsonSerializer.Deserialize<List<GitHubRelease>>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogError(ex, "Error during JSON deserialization. Actual response: {Content}", content);
                return null;
            }

            if (releases == null || releases.Count == 0)
            {
                _logger.LogDebug("No releases were deserialized or the list is empty.");
                return null;
            }

            _logger.LogDebug("Found {Count} releases", releases.Count);

            // Get the latest non-prerelease (like your working code)
            var latestRelease = releases.Where(r => !r.Prerelease).FirstOrDefault();
            if (latestRelease == null)
            {
                _logger.LogDebug("No suitable release found after filtering prereleases.");
                return null;
            }

            _logger.LogInformation("Found GitHub release: {TagName} with {AssetCount} assets", 
                latestRelease.TagName, latestRelease.Assets?.Count ?? 0);

            var result = ConvertToPluginInfo(latestRelease, owner, repo, pluginId, pluginName, assetNamePattern);
            
            if (result == null)
            {
                _logger.LogError("ConvertToPluginInfo returned null for {Owner}/{Repo} release {TagName}", 
                    owner, repo, latestRelease.TagName);
            }
            else
            {
                _logger.LogInformation("Successfully converted GitHub release to plugin info: {PluginId}, DownloadUrl: {DownloadUrl}", 
                    result.Id, result.DownloadUrl);
            }
            
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching releases for {Owner}/{Repo}", owner, repo);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch releases for {Owner}/{Repo}", owner, repo);
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

        _logger.LogInformation("Available assets: [{Assets}]", string.Join(", ", assets.Select(a => $"'{a.Name}'")));
        _logger.LogInformation("Asset name pattern: '{Pattern}'", assetNamePattern ?? "none (defaulting to .zip)");

        // If no pattern specified, default to first .zip file (like your working code)
        if (string.IsNullOrEmpty(assetNamePattern))
        {
            var zipAsset = assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (zipAsset != null)
            {
                _logger.LogInformation("Selected asset: '{AssetName}' (default .zip match), URL: {Url}", 
                    zipAsset.Name, zipAsset.BrowserDownloadUrl);
            }
            else
            {
                _logger.LogWarning("No .zip asset found in available assets");
            }
            return zipAsset;
        }

        // Use regex pattern matching
        try
        {
            var regex = new Regex(assetNamePattern, RegexOptions.IgnoreCase);
            _logger.LogDebug("Testing regex pattern '{Pattern}' against assets", assetNamePattern);
            
            foreach (var asset in assets)
            {
                var matches = regex.IsMatch(asset.Name);
                _logger.LogDebug("Asset '{AssetName}' matches pattern: {Matches}", asset.Name, matches);
            }
            
            var matchedAsset = assets.FirstOrDefault(a => regex.IsMatch(a.Name));
            if (matchedAsset != null)
            {
                _logger.LogInformation("Selected asset: '{AssetName}' (regex match), URL: {Url}", 
                    matchedAsset.Name, matchedAsset.BrowserDownloadUrl);
            }
            else
            {
                _logger.LogWarning("No asset matched regex pattern '{Pattern}'", assetNamePattern);
            }
            return matchedAsset;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Regex pattern '{Pattern}' failed, falling back to simple .zip search", assetNamePattern);
            // If regex fails, fall back to simple .zip search (like your working code)
            var zipAsset = assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (zipAsset != null)
            {
                _logger.LogInformation("Selected asset: '{AssetName}' (fallback .zip match), URL: {Url}", 
                    zipAsset.Name, zipAsset.BrowserDownloadUrl);
            }
            else
            {
                _logger.LogWarning("No .zip asset found in fallback search");
            }
            return zipAsset;
        }
    }
}