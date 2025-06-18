using MessagePack;

namespace PluginManager.Core.Models;

public class PluginCacheData
{
    [Key(0)]
    public List<PluginMod> Mods { get; set; } = new();

    [Key(1)]
    public DateTimeOffset ExpirationTime { get; set; }

    [Key(2)]
    public string PluginId { get; set; } = string.Empty;

}