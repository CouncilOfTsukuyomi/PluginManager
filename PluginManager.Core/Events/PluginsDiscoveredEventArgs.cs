using PluginManager.Core.Models;

namespace PluginManager.Core.Events;

public class PluginDiscoveredEventArgs : EventArgs
{
    public PluginInfo DiscoveredPlugin { get; }
    public int TotalDiscoveredCount { get; }
    public bool IsNewPlugin { get; }

    public PluginDiscoveredEventArgs(PluginInfo discoveredPlugin, int totalDiscoveredCount, bool isNewPlugin = false)
    {
        DiscoveredPlugin = discoveredPlugin;
        TotalDiscoveredCount = totalDiscoveredCount;
        IsNewPlugin = isNewPlugin;
    }
}