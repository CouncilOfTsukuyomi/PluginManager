using PluginManager.Core.Enums;

namespace PluginManager.Core.Models;

public class PluginMod
{
    public string Name { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string ModUrl { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public ModGender Gender { get; set; } = ModGender.Unisex;
    public string PluginSource { get; set; } = string.Empty;
    public Dictionary<string, object> AdditionalProperties { get; set; } = new();
}
