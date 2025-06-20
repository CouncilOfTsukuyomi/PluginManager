using System.Text.Json;
using Microsoft.Extensions.Logging;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;

namespace PluginManager.Core.Services;

public class DefaultPluginRegistryService : IDefaultPluginRegistryService
{
    private readonly ILogger<DefaultPluginRegistryService> _logger;
    private readonly string _registryUrl;
    private DefaultPluginRegistry? _cachedRegistry;
    private DateTime _lastCacheTime = DateTime.MinValue;
    private readonly TimeSpan _cacheTimeout = TimeSpan.FromMinutes(15);

    public DefaultPluginRegistryService(string registryUrl, ILogger<DefaultPluginRegistryService> logger)
    {
        _registryUrl = registryUrl ?? throw new ArgumentNullException(nameof(registryUrl));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DefaultPluginRegistry> GetRegistryAsync()
    {
        // Simple caching mechanism
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
            var registry = JsonSerializer.Deserialize<DefaultPluginRegistry>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (registry == null)
            {
                throw new InvalidOperationException("Failed to deserialize plugin registry");
            }

            _cachedRegistry = registry;
            _lastCacheTime = DateTime.UtcNow;
            
            _logger.LogInformation("Successfully loaded registry with {Count} plugins", registry.Plugins.Count);
            return registry;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch plugin registry from {Url}", _registryUrl);
            
            // Return cached version if available, otherwise empty registry
            if (_cachedRegistry != null)
            {
                _logger.LogWarning("Returning cached registry due to fetch failure");
                return _cachedRegistry;
            }
            
            return new DefaultPluginRegistry();
        }
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