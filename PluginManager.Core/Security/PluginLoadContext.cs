using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;

namespace PluginManager.Core.Security;

/// <summary>
/// Custom AssemblyLoadContext for plugin isolation
/// </summary>
public class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly ILogger _logger;

    public PluginLoadContext(string pluginPath, ILogger logger) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
        _logger = logger;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Try to resolve the assembly path
        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            _logger.LogDebug("Loading assembly {AssemblyName} from {Path}", assemblyName.Name, assemblyPath);
            return LoadFromAssemblyPath(assemblyPath);
        }

        // Let the default context handle system assemblies
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }
}