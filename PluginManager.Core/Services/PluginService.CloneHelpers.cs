using System.Collections.Generic;
using PluginManager.Core.Models;

namespace PluginManager.Core.Services
{
    internal static class PluginServiceCloneHelpers
    {
        public static PluginMod CloneMod(PluginMod mod, string pluginId)
        {
            if (mod == null)
            {
                return new PluginMod { PluginSource = pluginId };
            }

            return new PluginMod
            {
                Name = mod.Name,
                ModUrl = mod.ModUrl,
                DownloadUrl = mod.DownloadUrl,
                ImageUrl = mod.ImageUrl,
                Publisher = mod.Publisher,
                Type = mod.Type,
                Version = mod.Version,
                UploadDate = mod.UploadDate,
                FileSize = mod.FileSize,
                PluginSource = pluginId,
                Tags = mod.Tags != null ? new List<string>(mod.Tags) : new List<string>(),
                Metadata = mod.Metadata != null ? new Dictionary<string, object>(mod.Metadata) : new Dictionary<string, object>()
            };
        }
    }
}
