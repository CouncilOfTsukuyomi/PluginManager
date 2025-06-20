namespace PluginManager.Core.Models;

public class DefaultPluginRegistry
{
    public string Version { get; set; } = "1.0";
    public DateTime LastUpdated { get; set; }
    public List<DefaultPluginInfo> Plugins { get; set; } = new();
}
