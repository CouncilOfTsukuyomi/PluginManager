using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;
using PluginManager.Core.Security;

namespace PluginManager.Core.Services;

public class PluginDiscoveryService : IPluginDiscoveryService
{
    private readonly ILogger<PluginDiscoveryService> _logger;
    private readonly string _pluginsDirectory;

    public PluginDiscoveryService(ILogger<PluginDiscoveryService> logger, string pluginsBasePath)
    {
        _logger = logger;
        _pluginsDirectory = pluginsBasePath;

        // Ensure plugins directory exists
        Directory.CreateDirectory(_pluginsDirectory);
    }

    public async Task<List<PluginInfo>> DiscoverPluginsAsync()
    {
        var plugins = new List<PluginInfo>();

        if (!Directory.Exists(_pluginsDirectory))
        {
            _logger.LogWarning("Plugins directory does not exist: {PluginsDirectory}", _pluginsDirectory);
            return plugins;
        }

        // Scan for plugin directories
        var pluginDirectories = Directory.GetDirectories(_pluginsDirectory);

        foreach (var pluginDir in pluginDirectories)
        {
            try
            {
                var pluginInfo = await AnalyzePluginDirectoryAsync(pluginDir);
                if (pluginInfo != null)
                {
                    plugins.Add(pluginInfo);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze plugin directory: {PluginDirectory}", pluginDir);
            }
        }

        _logger.LogInformation("Discovered {Count} plugins in plugins directory", plugins.Count);
        return plugins;
    }

    public async Task<IModPlugin?> LoadPluginAsync(PluginInfo pluginInfo)
    {
        try
        {
            _logger.LogDebug("Loading plugin: {PluginId} from {AssemblyPath}", 
                pluginInfo.PluginId, pluginInfo.AssemblyPath);

            // Use IsolatedPluginLoader for safe assembly loading
            var loaderLogger = NullLogger<IsolatedPluginLoader>.Instance;
            using var loader = new IsolatedPluginLoader(loaderLogger, pluginInfo.PluginDirectory);
            var plugin = await loader.LoadPluginAsync(pluginInfo.AssemblyPath, pluginInfo.TypeName);

            if (plugin != null)
            {
                plugin.PluginDirectory = pluginInfo.PluginDirectory;
                plugin.IsEnabled = pluginInfo.IsEnabled;
                
                // Initialize with configuration
                await plugin.InitializeAsync(pluginInfo.Configuration);
                
                _logger.LogInformation("Successfully loaded plugin: {PluginId}", pluginInfo.PluginId);
            }

            return plugin;
        }
        catch (Exception ex)
        {
            var errorMessage = $"Failed to load plugin {pluginInfo.PluginId}: {ex.Message}";
            _logger.LogError(ex, errorMessage);
            
            pluginInfo.LoadError = errorMessage;
            pluginInfo.IsLoaded = false;
            
            return null;
        }
    }

    public async Task<List<PluginInfo>> GetAllPluginInfoAsync()
    {
        var discoveredPlugins = await DiscoverPluginsAsync();

        // Load settings for each plugin
        foreach (var plugin in discoveredPlugins)
        {
            try
            {
                var settings = await GetPluginSettingsAsync(plugin.PluginDirectory);
                plugin.IsEnabled = settings.IsEnabled;
                plugin.Configuration = settings.Configuration;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load settings for plugin {PluginId}", plugin.PluginId);
            }
        }

        return discoveredPlugins;
    }

    public async Task SetPluginEnabledAsync(string pluginId, bool enabled)
    {
        var plugins = await GetAllPluginInfoAsync();
        var plugin = plugins.FirstOrDefault(p => p.PluginId == pluginId);
        
        if (plugin == null)
        {
            _logger.LogWarning("Plugin {PluginId} not found", pluginId);
            return;
        }

        var settings = await GetPluginSettingsAsync(plugin.PluginDirectory);
        settings.IsEnabled = enabled;
        await SavePluginSettingsAsync(plugin.PluginDirectory, settings);
        
        _logger.LogInformation("Plugin {PluginId} {Status}", pluginId, enabled ? "enabled" : "disabled");
    }

    public async Task UpdatePluginConfigurationAsync(string pluginId, Dictionary<string, object> configuration)
    {
        var plugins = await GetAllPluginInfoAsync();
        var plugin = plugins.FirstOrDefault(p => p.PluginId == pluginId);
        
        if (plugin == null)
        {
            _logger.LogWarning("Plugin {PluginId} not found", pluginId);
            return;
        }

        var settings = await GetPluginSettingsAsync(plugin.PluginDirectory);
        settings.Configuration = configuration;
        await SavePluginSettingsAsync(plugin.PluginDirectory, settings);
        
        _logger.LogDebug("Updated configuration for plugin {PluginId}", pluginId);
    }

    public async Task<PluginSettings> GetPluginSettingsAsync(string pluginDirectory)
    {
        var settingsPath = Path.Combine(pluginDirectory, "plugin-settings.json");
        
        try
        {
            if (!File.Exists(settingsPath))
            {
                return new PluginSettings();
            }

            var json = await File.ReadAllTextAsync(settingsPath);
            var settings = JsonSerializer.Deserialize<PluginSettings>(json);
            return settings ?? new PluginSettings();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load plugin settings from {SettingsPath}", settingsPath);
            return new PluginSettings();
        }
    }

    public async Task SavePluginSettingsAsync(string pluginDirectory, PluginSettings settings)
    {
        var settingsPath = Path.Combine(pluginDirectory, "plugin-settings.json");
        
        try
        {
            settings.LastUpdated = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(settingsPath, json);
            
            _logger.LogDebug("Saved plugin settings to {SettingsPath}", settingsPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save plugin settings to {SettingsPath}", settingsPath);
        }
    }

    private async Task<PluginInfo?> AnalyzePluginDirectoryAsync(string pluginDirectory)
    {
        try
        {
            // Look for .dll files in the plugin directory, excluding PluginManager.Core.dll
            var dllFiles = Directory.GetFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                .Where(dll => !Path.GetFileName(dll).Equals("PluginManager.Core.dll", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            
            if (!dllFiles.Any())
            {
                _logger.LogDebug("No plugin DLL files found in plugin directory: {PluginDirectory}", pluginDirectory);
                return null;
            }

            // Try each DLL file to find one with IModPlugin implementations
            foreach (var dllFile in dllFiles)
            {
                try
                {
                    // Use IsolatedPluginLoader for safe discovery
                    var loaderLogger = NullLogger<IsolatedPluginLoader>.Instance;
                    using var loader = new IsolatedPluginLoader(loaderLogger, pluginDirectory);
                    
                    // Try to load and analyze the assembly
                    var pluginInfo = await TryDiscoverPluginFromAssembly(loader, dllFile, pluginDirectory);
                    if (pluginInfo != null)
                    {
                        return pluginInfo;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to analyze DLL file: {DllFile}", dllFile);
                    continue;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze plugin directory: {PluginDirectory}", pluginDirectory);
            return null;
        }
    }

    private async Task<PluginInfo?> TryDiscoverPluginFromAssembly(IsolatedPluginLoader loader, string dllFile, string pluginDirectory)
    {
        try
        {
            // Load assembly in isolation to discover plugin types
            var assembly = Assembly.LoadFrom(dllFile);
            var pluginTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(IModPlugin).IsAssignableFrom(t))
                .ToList();

            if (!pluginTypes.Any())
                return null;

            // Take the first plugin type found
            var pluginType = pluginTypes.First();
            
            // Load plugin instance to get metadata
            var tempInstance = await loader.LoadPluginAsync(dllFile, pluginType.FullName!);
            if (tempInstance == null)
                return null;

            var fileInfo = new FileInfo(dllFile);
            
            var pluginInfo = new PluginInfo
            {
                PluginId = tempInstance.PluginId,
                DisplayName = tempInstance.DisplayName,
                Description = tempInstance.Description,
                Version = tempInstance.Version,
                Author = tempInstance.Author,
                AssemblyPath = dllFile,
                TypeName = pluginType.FullName!,
                PluginDirectory = pluginDirectory,
                LastModified = fileInfo.LastWriteTime,
                IsLoaded = false
            };

            // Dispose temporary instance
            await tempInstance.DisposeAsync();

            return pluginInfo;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to discover plugin from assembly: {DllFile}", dllFile);
            return null;
        }
    }
}