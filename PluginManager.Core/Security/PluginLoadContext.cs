
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
        // CRITICAL: For core PluginManager assemblies, always use the main context
        // This ensures interface compatibility and constructor availability between plugin and host
        if (assemblyName.Name?.StartsWith("PluginManager.Core", StringComparison.OrdinalIgnoreCase) == true)
        {
            _logger.LogDebug("Using main context for core assembly: {AssemblyName}", assemblyName.Name);
            
            // Try to find the assembly in the main context first
            var loadedAssemblies = AssemblyLoadContext.Default.Assemblies;
            var existingAssembly = loadedAssemblies.FirstOrDefault(a => 
                string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
            
            if (existingAssembly != null)
            {
                _logger.LogDebug("Found core assembly {AssemblyName} in main context", assemblyName.Name);
                return existingAssembly;
            }
        }

        // Also ensure Microsoft.Extensions assemblies use the main context for consistency
        if (assemblyName.Name?.StartsWith("Microsoft.Extensions.", StringComparison.OrdinalIgnoreCase) == true)
        {
            _logger.LogDebug("Using main context for Microsoft.Extensions assembly: {AssemblyName}", assemblyName.Name);
            
            var loadedAssemblies = AssemblyLoadContext.Default.Assemblies;
            var existingAssembly = loadedAssemblies.FirstOrDefault(a => 
                string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
            
            if (existingAssembly != null)
            {
                _logger.LogDebug("Found Microsoft.Extensions assembly {AssemblyName} in main context", assemblyName.Name);
                return existingAssembly;
            }
        }

        // For other assemblies, try to resolve from the plugin directory first
        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            _logger.LogDebug("Loading assembly {AssemblyName} from {Path}", assemblyName.Name, assemblyPath);
            return LoadFromAssemblyPath(assemblyPath);
        }

        // Let the default context handle system assemblies and any we couldn't resolve
        _logger.LogDebug("Deferring to default context for assembly: {AssemblyName}", assemblyName.Name);
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