namespace PluginManager.Core.Models;

/// <summary>
/// Represents a plugin dependency
/// </summary>
public class PluginDependency
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool Optional { get; set; } = false;
    public string? MinVersion { get; set; }
    public string? MaxVersion { get; set; }
}