using System.Text.Json.Serialization;

namespace PluginManager.Core.Models.GitHub;

public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;
    
    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;
    
    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; }
    
    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }
    
    [JsonPropertyName("assets")]
    public List<GitHubAsset>? Assets { get; set; }
}