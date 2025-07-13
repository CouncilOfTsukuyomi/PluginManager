using PluginManager.Core.Models;

namespace PluginManager.Core.Events;

public class AllPluginsLoadedEventArgs : EventArgs
{
    public IReadOnlyList<PluginInfo> LoadedPlugins { get; }
    public IReadOnlyList<PluginInfo> FailedPlugins { get; }
    public TimeSpan TotalLoadTime { get; }
    public int TotalPluginCount { get; }
    public int SuccessfullyLoadedCount { get; }

    public AllPluginsLoadedEventArgs(
        IReadOnlyList<PluginInfo> loadedPlugins,
        IReadOnlyList<PluginInfo> failedPlugins,
        TimeSpan totalLoadTime)
    {
        LoadedPlugins = loadedPlugins;
        FailedPlugins = failedPlugins;
        TotalLoadTime = totalLoadTime;
        TotalPluginCount = loadedPlugins.Count + failedPlugins.Count;
        SuccessfullyLoadedCount = loadedPlugins.Count;
    }
}