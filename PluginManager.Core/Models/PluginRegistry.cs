namespace PluginManager.Core.Models;

/// <summary>
/// Global plugin registry that tracks all plugins and their state
/// </summary>
public class PluginRegistry
{
    public Dictionary<string, PluginRegistryEntry> Plugins { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public string Version { get; set; } = "1.0.0";
}
