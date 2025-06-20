namespace PluginManager.Core.Models;

public class PluginDownloadResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public byte[]? PluginData { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public string? ContentType { get; set; }
}