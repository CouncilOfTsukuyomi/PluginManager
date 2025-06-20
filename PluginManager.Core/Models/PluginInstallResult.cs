namespace PluginManager.Core.Models;

public class PluginInstallResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? InstalledPath { get; set; }
    public long FileSize { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public string PluginName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}