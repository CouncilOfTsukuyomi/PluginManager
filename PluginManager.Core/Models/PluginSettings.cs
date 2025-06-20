namespace PluginManager.Core.Models;

public class PluginSettings
{
    public bool IsEnabled { get; set; } = false;
    public Dictionary<string, object> Configuration { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public string Version { get; set; } = "1.0.0";
    public string SchemaVersion { get; set; } = "1.0.0";
    public Dictionary<string, object> Metadata { get; set; } = new();
    public Dictionary<string, object>? PreviousConfiguration { get; set; }
    public string? PreviousSchemaVersion { get; set; }
}