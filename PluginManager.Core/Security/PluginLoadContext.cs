using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;

namespace PluginManager.Core.Security;

/// <summary>
/// Custom AssemblyLoadContext for plugin isolation with smart assembly sharing
/// </summary>
public class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly ILogger _logger;
    private readonly string _mainPluginAssemblyPath;
    
    // Cache of assemblies already loaded in the default context
    private static readonly Lazy<HashSet<string>> _defaultContextAssemblies = new(() =>
    {
        return Default.Assemblies
            .Select(a => a.GetName().Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;
    });

    public PluginLoadContext(string pluginPath, ILogger logger) : base($"Plugin_{Path.GetFileNameWithoutExtension(pluginPath)}_{Guid.NewGuid():N}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
        _logger = logger;
        _mainPluginAssemblyPath = pluginPath;
    }
    
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        // PRIORITY: Always check if we should use the default context FIRST
        // This ensures we use the host's version of PluginManager.Core and other shared assemblies
        if (ShouldUseDefaultContext(name))
        {
            _logger.LogDebug("Deferring shared assembly to default context: {AssemblyName} (requested version: {Version})", 
                name, assemblyName.Version);
            
            // CRITICAL: For PluginManager.Core, handle version mismatches by finding ANY loaded version
            if (name.StartsWith("PluginManager.Core", StringComparison.OrdinalIgnoreCase))
            {
                var loadedAssembly = FindLoadedPluginManagerCore(assemblyName);
                if (loadedAssembly != null)
                {
                    _logger.LogDebug("Resolved PluginManager.Core version mismatch: requested {RequestedVersion}, providing {ActualVersion}",
                        assemblyName.Version, loadedAssembly.GetName().Version);
                    return loadedAssembly;
                }
            }
            
            return null;
        }

        // Only after confirming it's not a shared assembly, check if we have a local copy
        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            _logger.LogDebug("Loading plugin assembly {AssemblyName} from plugin directory: {Path}", name, assemblyPath);
            return LoadFromAssemblyPath(assemblyPath);
        }
        
        if (_defaultContextAssemblies.Value.Contains(name))
        {
            _logger.LogDebug("Assembly {AssemblyName} not found locally, deferring to default context", name);
            return null;
        }

        _logger.LogDebug("Unknown assembly {AssemblyName}, deferring to default context", name);
        return null;
    }

    /// <summary>
    /// Finds any loaded version of PluginManager.Core in the default context
    /// </summary>
    private Assembly? FindLoadedPluginManagerCore(AssemblyName requestedAssemblyName)
    {
        try
        {
            // First, try to find an exact match by name (ignoring version)
            var loadedAssembly = Default.Assemblies
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "PluginManager.Core", StringComparison.OrdinalIgnoreCase));

            if (loadedAssembly != null)
            {
                _logger.LogDebug("Found loaded PluginManager.Core assembly: {ActualVersion} for requested {RequestedVersion}",
                    loadedAssembly.GetName().Version, requestedAssemblyName.Version);
                return loadedAssembly;
            }

            // If not found, try to load it explicitly (this should trigger the host's version)
            try
            {
                var coreAssembly = typeof(PluginManager.Core.Interfaces.IModPlugin).Assembly;
                _logger.LogDebug("Using PluginManager.Core from IModPlugin type: {Version}", coreAssembly.GetName().Version);
                return coreAssembly;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get PluginManager.Core from IModPlugin type");
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find loaded PluginManager.Core assembly");
            return null;
        }
    }

    /// <summary>
    /// Determines whether an assembly should be loaded from the default context (shared)
    /// or from the plugin context (isolated)
    /// </summary>
    private bool ShouldUseDefaultContext(string assemblyName)
    {
        if (IsSystemAssembly(assemblyName))
        {
            return true;
        }
        
        if (assemblyName.StartsWith("PluginManager.Core", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsLoggingAbstraction(assemblyName))
        {
            return true;
        }
        
        if (IsCommonFrameworkAssembly(assemblyName))
        {
            return true;
        }
        
        string? localPath = _resolver.ResolveAssemblyToPath(new AssemblyName(assemblyName));
        if (localPath == null && _defaultContextAssemblies.Value.Contains(assemblyName))
        {
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// Check if this is a core .NET system assembly
    /// </summary>
    private static bool IsSystemAssembly(string name)
    {
        return name.Equals("System", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("System.Core", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("System.Runtime", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("netstandard", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("System.", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Check if this is a logging abstraction that should be shared
    /// </summary>
    private static bool IsLoggingAbstraction(string name)
    {
        return name.Equals("Microsoft.Extensions.Logging.Abstractions", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Microsoft.Extensions.DependencyInjection.Abstractions", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Microsoft.Extensions.Logging", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Check if this is a common Microsoft framework assembly
    /// </summary>
    private static bool IsCommonFrameworkAssembly(string name)
    {
        var commonPrefixes = new[]
        {
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.Options",
            "Microsoft.Extensions.Primitives",
            "Microsoft.Extensions.FileProviders",
            "Microsoft.AspNetCore.Http.Abstractions",
            "Newtonsoft.Json",
            "System.Text.Json"
        };

        return commonPrefixes.Any(prefix => 
            name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
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