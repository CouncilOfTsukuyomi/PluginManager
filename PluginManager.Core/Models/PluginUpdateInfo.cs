namespace PluginManager.Core.Models;

public class PluginUpdateInfo
{
    public required DefaultPluginInfo CurrentPlugin { get; set; }
    public required DefaultPluginInfo LatestPlugin { get; set; }
    public bool UpdateAvailable { get; set; }
}