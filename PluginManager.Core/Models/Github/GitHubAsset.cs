using System.Text.Json.Serialization;

namespace PluginManager.Core.Models.GitHub;

public class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;
    
    [JsonPropertyName("size")]
    public long Size { get; set; }
    
    [JsonPropertyName("content_type")]
    public string ContentType { get; set; } = string.Empty;
    
    [JsonPropertyName("download_count")]
    public int DownloadCount { get; set; }
}