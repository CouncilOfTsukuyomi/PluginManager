
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
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }
        
        if (ShouldUseDefaultContext(name))
        {
            _logger.LogDebug("Deferring shared assembly to default context: {AssemblyName}", name);
            return null;
        }
        
        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            _logger.LogDebug("Loading plugin assembly {AssemblyName} from {Path}", name, assemblyPath);
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
    /// Determines whether an assembly should be loaded from the default context (shared)
    /// or from the plugin context (isolated)
    /// </summary>
    private bool ShouldUseDefaultContext(string assemblyName)
    {
        if (IsSystemAssembly(assemblyName))
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
        
        if (_defaultContextAssemblies.Value.Contains(assemblyName))
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
               name.Equals("Microsoft.Extensions.DependencyInjection.Abstractions", StringComparison.OrdinalIgnoreCase);
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