using PluginManager.Core.Enums;

namespace PluginManager.Core.Models;

/// <summary>
/// Registry entry for a single plugin with integrity tracking and discovery metadata only
/// </summary>
public class PluginRegistryEntry
{
    public string PluginId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string AssemblyPath { get; set; } = string.Empty;
    public string AssemblyHash { get; set; } = string.Empty;
    public DateTime LastLoaded { get; set; }
    public DateTime LastModified { get; set; }
    public long AssemblySize { get; set; }
    public PluginIntegrityStatus IntegrityStatus { get; set; } = PluginIntegrityStatus.Unknown;
    public string? LastError { get; set; }
    public int LoadCount { get; set; }
    public TimeSpan TotalRuntime { get; set; }
}