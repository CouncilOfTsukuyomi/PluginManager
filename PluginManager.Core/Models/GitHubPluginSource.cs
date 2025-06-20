namespace PluginManager.Core.Models;

public class GitHubPluginSource
{
    public required string Owner { get; set; }
    public required string Repository { get; set; }
    public bool IncludePrereleases { get; set; } = false;
    
    /// <summary>
    /// Optional: Override category from GitHub source
    /// </summary>
    public string? CategoryOverride { get; set; }
    
    /// <summary>
    /// Optional: Additional tags to append from GitHub source
    /// </summary>
    public List<string> AdditionalTags { get; set; } = new();
    
    /// <summary>
    /// Optional: Custom asset name pattern (default: *.zip)
    /// </summary>
    public string? AssetNamePattern { get; set; }
}