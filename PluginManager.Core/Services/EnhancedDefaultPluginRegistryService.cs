
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;

namespace PluginManager.Core.Services;

public class EnhancedDefaultPluginRegistryService : IDefaultPluginRegistryService
{
    private readonly ILogger<EnhancedDefaultPluginRegistryService> _logger;
    private readonly string _registryUrl;
    private readonly IGitHubPluginProvider _gitHubProvider;
    private DefaultPluginRegistry? _cachedRegistry;
    private DateTime _lastCacheTime = DateTime.MinValue;
    private readonly TimeSpan _cacheTimeout = TimeSpan.FromMinutes(15);

    public EnhancedDefaultPluginRegistryService(
        string registryUrl, 
        ILogger<EnhancedDefaultPluginRegistryService> logger,
        IGitHubPluginProvider gitHubProvider)
    {
        _registryUrl = registryUrl ?? throw new ArgumentNullException(nameof(registryUrl));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gitHubProvider = gitHubProvider ?? throw new ArgumentNullException(nameof(gitHubProvider));
    }

    public async Task<DefaultPluginRegistry> GetRegistryAsync()
    {
        if (_cachedRegistry != null && DateTime.UtcNow - _lastCacheTime < _cacheTimeout)
        {
            _logger.LogDebug("Returning cached plugin registry");
            return _cachedRegistry;
        }

        try
        {
            _logger.LogInformation("Fetching plugin registry from {Url}", _registryUrl);
            
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            
            var response = await httpClient.GetStringAsync(_registryUrl);
            var registry = JsonSerializer.Deserialize<EnhancedPluginRegistry>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (registry == null)
            {
                throw new InvalidOperationException("Failed to deserialize plugin registry");
            }

            // Process plugins with GitHub sources
            await ProcessPluginsWithGitHubSourcesAsync(registry);

            _cachedRegistry = new DefaultPluginRegistry
            {
                Version = registry.Version,
                LastUpdated = DateTime.UtcNow,
                Plugins = registry.Plugins.ToList()
            };
            _lastCacheTime = DateTime.UtcNow;
            
            _logger.LogInformation("Successfully loaded registry with {Count} plugins", _cachedRegistry.Plugins.Count);
            return _cachedRegistry;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch plugin registry from {Url}", _registryUrl);
            
            if (_cachedRegistry != null)
            {
                _logger.LogWarning("Returning cached registry due to fetch failure");
                return _cachedRegistry;
            }
            
            return new DefaultPluginRegistry();
        }
    }

    private async Task ProcessPluginsWithGitHubSourcesAsync(EnhancedPluginRegistry registry)
    {
        var pluginsWithGitHubSources = registry.Plugins
            .Where(p => p.GitHubSource != null)
            .ToList();

        foreach (var plugin in pluginsWithGitHubSources)
        {
            try
            {
                var gitHubSource = plugin.GitHubSource!;
                var latestPlugin = await _gitHubProvider.GetLatestPluginAsync(
                    gitHubSource.Owner, 
                    gitHubSource.Repository, 
                    plugin.Id, 
                    plugin.Name,
                    gitHubSource.AssetNamePattern); // Add this parameter

                if (latestPlugin != null)
                {
                    // Update plugin with GitHub data while preserving original configuration
                    UpdatePluginWithGitHubData(plugin, latestPlugin, gitHubSource);
                    
                    _logger.LogDebug("Updated plugin {PluginId} with GitHub data, version {Version}", 
                        plugin.Id, plugin.Version);
                }
                else
                {
                    _logger.LogWarning("Failed to fetch GitHub data for plugin {PluginId} from {Owner}/{Repo}", 
                        plugin.Id, gitHubSource.Owner, gitHubSource.Repository);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process GitHub source for plugin {PluginId}", plugin.Id);
            }
        }
    }

    private static void UpdatePluginWithGitHubData(
        DefaultPluginInfo originalPlugin, 
        DefaultPluginInfo gitHubPlugin, 
        GitHubPluginSource gitHubSource)
    {
        // Update dynamic fields from GitHub
        originalPlugin.Version = gitHubPlugin.Version;
        originalPlugin.DownloadUrl = gitHubPlugin.DownloadUrl;
        originalPlugin.SizeInBytes = gitHubPlugin.SizeInBytes;
        originalPlugin.LastUpdated = gitHubPlugin.LastUpdated;
        
        // Update description if original is empty or placeholder
        if (string.IsNullOrEmpty(originalPlugin.Description) || 
            originalPlugin.Description.Contains("placeholder", StringComparison.OrdinalIgnoreCase))
        {
            originalPlugin.Description = gitHubPlugin.Description;
        }

        // Apply category override if specified
        if (!string.IsNullOrEmpty(gitHubSource.CategoryOverride))
        {
            originalPlugin.Category = gitHubSource.CategoryOverride;
        }
        
        // Merge tags (original + additional + GitHub tags, remove duplicates)
        var allTags = originalPlugin.Tags
            .Union(gitHubSource.AdditionalTags)
            .Union(gitHubPlugin.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        originalPlugin.Tags = allTags;

        // Merge metadata
        foreach (var kvp in gitHubPlugin.Metadata)
        {
            originalPlugin.Metadata[kvp.Key] = kvp.Value;
        }
        
        // Add GitHub source info to metadata
        originalPlugin.Metadata["githubSourceConfigured"] = true;
        originalPlugin.Metadata["githubOwner"] = gitHubSource.Owner;
        originalPlugin.Metadata["githubRepository"] = gitHubSource.Repository;
    }

    public async Task<IEnumerable<DefaultPluginInfo>> GetAvailablePluginsAsync()
    {
        var registry = await GetRegistryAsync();
        return registry.Plugins;
    }

    public async Task<DefaultPluginInfo?> GetPluginAsync(string pluginId)
    {
        var registry = await GetRegistryAsync();
        return registry.Plugins.FirstOrDefault(p => p.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IEnumerable<DefaultPluginInfo>> SearchPluginsAsync(string searchTerm)
    {
        var registry = await GetRegistryAsync();
        return registry.Plugins.Where(p => 
            p.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
            p.Description.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
            p.Tags.Any(tag => tag.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
        );
    }

    public async Task<IEnumerable<DefaultPluginInfo>> GetPluginsByCategoryAsync(string category)
    {
        var registry = await GetRegistryAsync();
        return registry.Plugins.Where(p => 
            p.Category?.Equals(category, StringComparison.OrdinalIgnoreCase) == true
        );
    }

    public void ClearCache()
    {
        _cachedRegistry = null;
        _lastCacheTime = DateTime.MinValue;
        _logger.LogDebug("Plugin registry cache cleared");
    }
}