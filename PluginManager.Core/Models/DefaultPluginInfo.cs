namespace PluginManager.Core.Models;

public class DefaultPluginInfo
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? Category { get; set; }
    public List<string> Tags { get; set; } = new();
    public string DownloadUrl { get; set; } = string.Empty;
    public long SizeInBytes { get; set; }
    public DateTime LastUpdated { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    /// <summary>
    /// GitHub source configuration for dynamic plugin fetching
    /// </summary>
    public GitHubPluginSource? GitHubSource { get; set; }
}