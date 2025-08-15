namespace PluginManager.Core.Models;

public class PluginMod
{
    /// <summary>
    /// Name of the mod
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// URL to the mod page
    /// </summary>
    public string ModUrl { get; set; } = "";

    /// <summary>
    /// Direct download URL for the mod
    /// </summary>
    public string DownloadUrl { get; set; } = "";

    /// <summary>
    /// URL to the mod's preview image
    /// </summary>
    public string ImageUrl { get; set; } = "";

    /// <summary>
    /// Publisher/author of the mod
    /// </summary>
    public string Publisher { get; set; } = "";

    /// <summary>
    /// Type/category of the mod
    /// </summary>
    public string Type { get; set; } = "";

    /// <summary>
    /// Version of the mod
    /// </summary>
    public string Version { get; set; } = "";

    /// <summary>
    /// When the mod was uploaded/published
    /// </summary>
    public DateTime UploadDate { get; set; }

    /// <summary>
    /// Size of the mod file in bytes
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// ID of the plugin that found this mod
    /// </summary>
    public string PluginSource { get; set; } = "";

    /// <summary>
    /// Tags associated with the mod
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Additional metadata about the mod
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}