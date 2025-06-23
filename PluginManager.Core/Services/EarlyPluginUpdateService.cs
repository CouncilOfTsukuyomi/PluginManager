
using Microsoft.Extensions.Logging;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;

namespace PluginManager.Core.Services;

public class EarlyPluginUpdateService : IEarlyPluginUpdateService
{
    private readonly ILogger<EarlyPluginUpdateService> _logger;
    private readonly IDefaultPluginRegistryService _registryService;
    private readonly IPluginDownloader _pluginDownloader;
    private readonly string _pluginsBasePath;

    public EarlyPluginUpdateService(
        ILogger<EarlyPluginUpdateService> logger,
        IDefaultPluginRegistryService registryService,
        IPluginDownloader pluginDownloader,
        string? pluginsBasePath = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _registryService = registryService ?? throw new ArgumentNullException(nameof(registryService));
        _pluginDownloader = pluginDownloader ?? throw new ArgumentNullException(nameof(pluginDownloader));
        _pluginsBasePath = pluginsBasePath ?? Path.Combine(AppContext.BaseDirectory, "plugins");
    }

    public async Task CheckAndInstallNewPluginsAsync()
    {
        try
        {
            _logger.LogInformation("EarlyPluginUpdateService: Starting pre-load plugin check...");
            
            var installedPluginIds = await GetInstalledPluginIdsAsync();
            _logger.LogDebug("EarlyPluginUpdateService: Found {Count} installed plugins", installedPluginIds.Count);
            
            var registryPlugins = await _registryService.GetAvailablePluginsAsync();
            _logger.LogInformation("EarlyPluginUpdateService: Found {Count} registry plugins", registryPlugins.Count());
            
            var newPluginsInstalled = 0;
            
            foreach (var registryPlugin in registryPlugins)
            {
                try
                {
                    if (!installedPluginIds.Contains(registryPlugin.Id, StringComparer.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("EarlyPluginUpdateService: Installing new plugin: {PluginName} (v{Version})", 
                            registryPlugin.Name, registryPlugin.Version);

                        var installResult = await _pluginDownloader.DownloadAndInstallAsync(registryPlugin);
                        
                        if (installResult.Success)
                        {
                            _logger.LogInformation("EarlyPluginUpdateService: Successfully installed plugin: {PluginName}", 
                                installResult.PluginName);
                            newPluginsInstalled++;
                        }
                        else
                        {
                            _logger.LogError("EarlyPluginUpdateService: Failed to install plugin {PluginName}: {Error}", 
                                installResult.PluginName, installResult.ErrorMessage);
                        }
                    }
                    else
                    {
                        _logger.LogDebug("EarlyPluginUpdateService: Plugin {PluginName} already installed, skipping", 
                            registryPlugin.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "EarlyPluginUpdateService: Error processing plugin {PluginName}", 
                        registryPlugin.Name);
                }
            }
            
            _logger.LogInformation("EarlyPluginUpdateService: Pre-load plugin check completed. Installed {Count} new plugins", 
                newPluginsInstalled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EarlyPluginUpdateService: Failed during pre-load plugin check");
        }
    }
    
    public async Task CheckForPluginUpdatesAsync()
    {
        try
        {
            _logger.LogInformation("EarlyPluginUpdateService: Starting pre-load plugin update check...");
            
            var installedPlugins = await GetInstalledPluginInfoAsync();
            _logger.LogDebug("EarlyPluginUpdateService: Found {Count} installed plugins for update check", installedPlugins.Count);
            
            var registryPlugins = await _registryService.GetAvailablePluginsAsync();
            var registryLookup = registryPlugins.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
            
            var updatesInstalled = 0;
            
            foreach (var installedPlugin in installedPlugins)
            {
                try
                {
                    if (registryLookup.TryGetValue(installedPlugin.PluginId, out var registryPlugin))
                    {
                        if (IsNewerVersion(registryPlugin.Version, installedPlugin.Version))
                        {
                            _logger.LogInformation("EarlyPluginUpdateService: Update available for {PluginName}: {CurrentVersion} -> {NewVersion}", 
                                installedPlugin.DisplayName, installedPlugin.Version, registryPlugin.Version);
                            
                            try
                            {
                                _logger.LogInformation("EarlyPluginUpdateService: Auto-updating plugin {PluginName}...", installedPlugin.DisplayName);
                                
                                // Remember if the plugin was enabled before the update
                                var wasEnabled = installedPlugin.IsEnabled;
                                var existingConfiguration = new Dictionary<string, object>(installedPlugin.Configuration);
                                
                                _logger.LogDebug("EarlyPluginUpdateService: Plugin {PluginName} was previously {State}", 
                                    installedPlugin.DisplayName, wasEnabled ? "enabled" : "disabled");
                                
                                var updateResult = await _pluginDownloader.DownloadAndInstallAsync(registryPlugin, _pluginsBasePath);
                                
                                if (updateResult.Success)
                                {
                                    _logger.LogInformation("EarlyPluginUpdateService: Successfully updated plugin {PluginName} from {OldVersion} to {NewVersion}", 
                                        installedPlugin.DisplayName, installedPlugin.Version, registryPlugin.Version);
                                    
                                    // Restore the enabled state after update
                                    await RestorePluginStateAfterUpdateAsync(
                                        updateResult.InstalledPath, 
                                        wasEnabled, 
                                        existingConfiguration,
                                        registryPlugin.Version);
                                    
                                    _logger.LogInformation("EarlyPluginUpdateService: Restored plugin {PluginName} to {State} state", 
                                        installedPlugin.DisplayName, wasEnabled ? "enabled" : "disabled");
                                    
                                    updatesInstalled++;
                                }
                                else
                                {
                                    _logger.LogError("EarlyPluginUpdateService: Failed to update plugin {PluginName}: {Error}", 
                                        installedPlugin.DisplayName, updateResult.ErrorMessage);
                                }
                            }
                            catch (Exception updateEx)
                            {
                                _logger.LogError(updateEx, "EarlyPluginUpdateService: Exception during auto-update of plugin {PluginName}", 
                                    installedPlugin.DisplayName);
                            }
                        }
                        else
                        {
                            _logger.LogDebug("EarlyPluginUpdateService: Plugin {PluginName} is up to date ({Version})", 
                                installedPlugin.DisplayName, installedPlugin.Version);
                        }
                    }
                    else
                    {
                        _logger.LogDebug("EarlyPluginUpdateService: Plugin {PluginName} not found in registry", 
                            installedPlugin.DisplayName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "EarlyPluginUpdateService: Error checking updates for plugin {PluginName}", 
                        installedPlugin.DisplayName);
                }
            }
            
            _logger.LogInformation("EarlyPluginUpdateService: Plugin update check completed. Installed {Count} updates", 
                updatesInstalled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EarlyPluginUpdateService: Failed during plugin update check");
        }
    }

    private async Task<HashSet<string>> GetInstalledPluginIdsAsync()
    {
        var pluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        try
        {
            if (!Directory.Exists(_pluginsBasePath))
            {
                _logger.LogDebug("EarlyPluginUpdateService: Plugins directory does not exist: {Path}", _pluginsBasePath);
                return pluginIds;
            }

            var pluginDirectories = Directory.GetDirectories(_pluginsBasePath);
            _logger.LogDebug("EarlyPluginUpdateService: Scanning {Count} plugin directories", pluginDirectories.Length);

            foreach (var pluginDir in pluginDirectories)
            {
                try
                {
                    var pluginInfo = await GetPluginInfoFromDirectoryAsync(pluginDir);
                    if (pluginInfo != null)
                    {
                        pluginIds.Add(pluginInfo.PluginId);
                        _logger.LogDebug("EarlyPluginUpdateService: Found plugin {PluginId} in {Directory}", 
                            pluginInfo.PluginId, Path.GetFileName(pluginDir));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "EarlyPluginUpdateService: Failed to read plugin info from {Directory}", 
                        Path.GetFileName(pluginDir));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EarlyPluginUpdateService: Failed to scan plugins directory: {Path}", _pluginsBasePath);
        }

        return pluginIds;
    }

    private async Task<List<PluginInfo>> GetInstalledPluginInfoAsync()
    {
        var plugins = new List<PluginInfo>();
        
        try
        {
            if (!Directory.Exists(_pluginsBasePath))
            {
                return plugins;
            }

            var pluginDirectories = Directory.GetDirectories(_pluginsBasePath);

            foreach (var pluginDir in pluginDirectories)
            {
                try
                {
                    var pluginInfo = await GetPluginInfoFromDirectoryAsync(pluginDir);
                    if (pluginInfo != null)
                    {
                        // Load the actual enabled state from plugin-settings.json if it exists
                        pluginInfo.IsEnabled = await GetPluginEnabledStateAsync(pluginDir);
                        plugins.Add(pluginInfo);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "EarlyPluginUpdateService: Failed to read plugin info from {Directory}", 
                        Path.GetFileName(pluginDir));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EarlyPluginUpdateService: Failed to scan plugins for update check");
        }

        return plugins;
    }

    private async Task<PluginInfo?> GetPluginInfoFromDirectoryAsync(string pluginDirectory)
    {
        try
        {
            var pluginJsonPath = Path.Combine(pluginDirectory, "plugin.json");
            
            if (!File.Exists(pluginJsonPath))
            {
                _logger.LogDebug("EarlyPluginUpdateService: No plugin.json found in {Directory}", 
                    Path.GetFileName(pluginDirectory));
                return null;
            }

            var jsonContent = await File.ReadAllTextAsync(pluginJsonPath);
            var pluginInfo = System.Text.Json.JsonSerializer.Deserialize<PluginInfo>(jsonContent, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (pluginInfo != null)
            {
                pluginInfo.PluginDirectory = pluginDirectory;
            }

            return pluginInfo;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EarlyPluginUpdateService: Failed to parse plugin.json in {Directory}", 
                Path.GetFileName(pluginDirectory));
            return null;
        }
    }

    private async Task<bool> GetPluginEnabledStateAsync(string pluginDirectory)
    {
        try
        {
            var settingsPath = Path.Combine(pluginDirectory, "plugin-settings.json");
            
            if (!File.Exists(settingsPath))
            {
                return false;
            }

            var jsonContent = await File.ReadAllTextAsync(settingsPath);
            using var document = System.Text.Json.JsonDocument.Parse(jsonContent);
            
            if (document.RootElement.TryGetProperty("isEnabled", out var enabledElement))
            {
                return enabledElement.GetBoolean();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EarlyPluginUpdateService: Failed to read enabled state from {PluginDirectory}", 
                Path.GetFileName(pluginDirectory));
        }

        return false;
    }

    private async Task RestorePluginStateAfterUpdateAsync(
        string pluginDirectory, 
        bool wasEnabled, 
        Dictionary<string, object> previousConfiguration,
        string newVersion)
    {
        try
        {
            var settingsPath = Path.Combine(pluginDirectory, "plugin-settings.json");
            
            var settings = new
            {
                isEnabled = wasEnabled,
                configuration = previousConfiguration,
                version = newVersion,
                schemaVersion = newVersion,
                lastUpdated = DateTime.UtcNow.ToString("O"),
                metadata = new Dictionary<string, object>
                {
                    {"updatedAt", DateTime.UtcNow.ToString("O")},
                    {"restoredEnabledState", wasEnabled},
                    {"configurationPreserved", true}
                }
            };

            var jsonContent = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });

            await File.WriteAllTextAsync(settingsPath, jsonContent);
            
            _logger.LogDebug("EarlyPluginUpdateService: Successfully restored settings for plugin in {PluginDirectory}", 
                Path.GetFileName(pluginDirectory));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EarlyPluginUpdateService: Failed to restore plugin state for {PluginDirectory}", 
                Path.GetFileName(pluginDirectory));
        }
    }

    private bool IsNewerVersion(string newVersion, string currentVersion)
    {
        try
        {
            var normalizedNewVersion = NormalizeVersion(newVersion);
            var normalizedCurrentVersion = NormalizeVersion(currentVersion);
        
            var newVer = new Version(normalizedNewVersion);
            var currentVer = new Version(normalizedCurrentVersion);
            return newVer > currentVer;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EarlyPluginUpdateService: Failed to compare versions {NewVersion} vs {CurrentVersion}", 
                newVersion, currentVersion);
            return false;
        }
    }

    private string NormalizeVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "0.0.0";
        
        var trimmed = version.Trim();
        
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(1);
        }
        
        return trimmed;
    }
}