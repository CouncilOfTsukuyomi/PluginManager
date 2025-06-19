namespace PluginManager.Core.Models;

public class PluginSettings
{
    public bool IsEnabled { get; set; } = false;
    public Dictionary<string, object> Configuration { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public string Version { get; set; } = string.Empty;
}