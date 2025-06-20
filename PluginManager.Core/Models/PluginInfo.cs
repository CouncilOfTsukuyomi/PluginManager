namespace PluginManager.Core.Models;

public class PluginInfo
{
    public string PluginId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string AssemblyPath { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string PluginDirectory { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = false;
    public Dictionary<string, object> Configuration { get; set; } = new();
    public string Author { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public bool IsLoaded { get; set; }
    public string? LoadError { get; set; }

}