namespace PluginManager.Core.Models;

/// <summary>
/// Configuration options for comprehensive plugin services
/// </summary>
public class PluginServicesOptions
{
    /// <summary>
    /// Base path for plugin storage (optional, defaults to "plugins" folder in app directory)
    /// </summary>
    public string? PluginBasePath { get; set; }
    
    /// <summary>
    /// URL for the plugin registry
    /// </summary>
    public string RegistryUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// Optional configuration for the HttpClient used by GitHub integration
    /// </summary>
    public Action<HttpClient>? ConfigureHttpClient { get; set; }
}