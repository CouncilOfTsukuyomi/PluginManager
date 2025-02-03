using System.Collections.Generic;
using System.Runtime.Loader;

namespace PluginManager.Core.Interfaces;

public interface IPluginManager
{
    List<AssemblyLoadContext> LoadAddons();
    List<IPlugin> DiscoverPlugins(List<AssemblyLoadContext> contexts);
    List<string> DiscoverPluginNames(List<AssemblyLoadContext> contexts);
}