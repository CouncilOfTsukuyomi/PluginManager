using System.Reflection;
using System.Runtime.Loader;

namespace PluginManager.Core.Services;

/// <summary>
/// Custom AssemblyLoadContext for loading plugin assemblies
/// from a dedicated folder. Each plugin has its own context
/// to isolate dependencies.
/// </summary>
public class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _pluginDirectory;

    public PluginLoadContext(string pluginDirectory, bool isCollectible = false)
        : base(isCollectible)
    {
        _pluginDirectory = pluginDirectory;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Check if already loaded
        var assembly = Assemblies.FirstOrDefault(a => a.FullName == assemblyName.FullName);
        if (assembly != null)
            return assembly;

        // Attempt to load from the plugin directory
        var candidatePath = Path.Combine(_pluginDirectory, assemblyName.Name + ".dll");
        return File.Exists(candidatePath) ? LoadFromAssemblyPath(candidatePath) :
            null;
    }
}