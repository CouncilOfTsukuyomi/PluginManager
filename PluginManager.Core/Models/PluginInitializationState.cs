namespace PluginManager.Core.Models;

/// <summary>
/// Tracks the initialization state of a plugin to avoid unnecessary reinitializations
/// </summary>
internal class PluginInitializationState
{
    public bool IsInitialized { get; set; }
    public string ConfigurationHash { get; set; } = string.Empty;
    public DateTime LastInitialized { get; set; }
}