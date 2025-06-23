namespace PluginManager.Core.Interfaces;

/// <summary>
/// Interface for checking and updating plugins BEFORE any plugin assemblies are loaded.
/// This avoids .NET 9 assembly unloading issues by performing updates while plugins are not in memory.
/// </summary>
public interface IEarlyPluginUpdateService
{
    /// <summary>
    /// Check for and install new plugins from the registry before any plugins are loaded
    /// </summary>
    Task CheckAndInstallNewPluginsAsync();

    /// <summary>
    /// Check for plugin updates without loading assemblies
    /// </summary>
    Task CheckForPluginUpdatesAsync();
}