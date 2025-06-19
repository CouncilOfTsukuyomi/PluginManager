namespace PluginManager.Core.Models;

/// <summary>
/// Plugin metadata structure matching plugin.json
/// </summary>
public class PluginMetadata
{
    public string PluginId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Website { get; set; }
    public string? RepositoryUrl { get; set; }
    public string AssemblyName { get; set; } = string.Empty;
    public string MainClass { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public string[]? Tags { get; set; }
    public string? Category { get; set; }
    public bool Featured { get; set; }
    public bool Verified { get; set; }
    public string? MinimumCoreVersion { get; set; }
    public string? TargetFramework { get; set; }
    public PluginDependency[]? Dependencies { get; set; }
    public object? Configuration { get; set; }
    public string[]? Permissions { get; set; }
    public string[]? SupportedPlatforms { get; set; }
}