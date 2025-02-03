using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using PluginManager.Core.Interfaces;

namespace PluginManager.Core.Services;

public class PluginManager : IPluginManager
{
    private readonly string _addonsDir;
    private readonly Dictionary<string, PluginLoadContext> _contexts;
    private FileSystemWatcher? _watcher;

    public PluginManager()
    {
        _addonsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Addons");
        _contexts = new Dictionary<string, PluginLoadContext>(StringComparer.OrdinalIgnoreCase);
            
        if (Directory.Exists(_addonsDir))
        {
            SetUpWatcher(_addonsDir);
        }
    }
    
    public List<AssemblyLoadContext> LoadAddons()
    {
        var loadContexts = new List<AssemblyLoadContext>();

        if (!Directory.Exists(_addonsDir))
            return loadContexts;

        foreach (var addonDir in Directory.EnumerateDirectories(_addonsDir))
        {
            var context = LoadAddonsFromDirectory(addonDir);
            if (context != null)
                loadContexts.Add(context);
        }

        return loadContexts;
    }
    
    public List<IPlugin> DiscoverPlugins(List<AssemblyLoadContext> contexts)
    {
        var plugins = new List<IPlugin>();

        foreach (var ctx in contexts)
        {
            foreach (var asm in ctx.Assemblies)
            {
                var candidates = asm.GetTypes()
                    .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract);

                foreach (var candidate in candidates)
                {
                    if (Activator.CreateInstance(candidate) is IPlugin pluginInstance)
                    {
                        plugins.Add(pluginInstance);
                    }
                }
            }
        }

        return plugins;
    }
    
    public List<string> DiscoverPluginNames(List<AssemblyLoadContext> contexts)
    {
        var plugins = DiscoverPlugins(contexts);
        var pluginNames = new List<string>();

        foreach (var plugin in plugins)
        {
            pluginNames.Add(plugin.PluginName);
        }

        return pluginNames;
    }
    
    public List<string> ExecuteAllPlugins(List<AssemblyLoadContext> contexts)
    {
        var plugins = DiscoverPlugins(contexts);
        var executedPluginNames = new List<string>();

        foreach (var plugin in plugins)
        {
            try
            {
                plugin.Execute();
                executedPluginNames.Add(plugin.PluginName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Plugin '{plugin.PluginName}' failed to execute: {ex.Message}");
            }
        }

        return executedPluginNames;
    }

    private PluginLoadContext? LoadAddonsFromDirectory(string addonDir)
    {
        if (!_contexts.TryGetValue(addonDir, out var context))
        {
            context = new PluginLoadContext(addonDir);
            _contexts[addonDir] = context;
        }

        foreach (var dllPath in Directory.EnumerateFiles(addonDir, "*.dll"))
        {
            context.LoadFromAssemblyPath(dllPath);
        }

        return context;
    }

    private void SetUpWatcher(string path)
    {
        _watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            Filter = "*.*",
            EnableRaisingEvents = true
        };

        // Trigger when new files or directories are created
        _watcher.Created += (sender, args) =>
        {
            if (Directory.Exists(args.FullPath))
            {
                LoadAddonsFromDirectory(args.FullPath);
            }
            else if (File.Exists(args.FullPath) 
                     && Path.GetExtension(args.FullPath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
            {
                var directory = Path.GetDirectoryName(args.FullPath);
                if (directory != null)
                {
                    LoadAddonsFromDirectory(directory);
                }
            }
        };
    }
}