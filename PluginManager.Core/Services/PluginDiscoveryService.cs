using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PluginManager.Core.Enums;
using PluginManager.Core.Events;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;
using PluginManager.Core.Security;

namespace PluginManager.Core.Services;

public class PluginDiscoveryService : IPluginDiscoveryService
{
    private readonly ILogger<PluginDiscoveryService> _logger;
    private readonly string _pluginsDirectory;
    private readonly PluginRegistryService _registryService;
    private readonly JsonSerializerOptions _jsonOptions;

    public event EventHandler<AllPluginsLoadedEventArgs>? AllPluginsLoaded;
    public event EventHandler<PluginDiscoveredEventArgs>? PluginDiscovered;
    
    public PluginDiscoveryService(
        ILogger<PluginDiscoveryService> logger, 
        string pluginsBasePath,
        PluginRegistryService registryService)
    {
        _logger = logger;
        _pluginsDirectory = pluginsBasePath;
        _registryService = registryService;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        Directory.CreateDirectory(_pluginsDirectory);
    }

    public async Task LoadAllEnabledPluginsAsync()
    {
        var startTime = DateTime.UtcNow;
        var loadedPlugins = new List<PluginInfo>();
        var failedPlugins = new List<PluginInfo>();

        try
        {
            _logger.LogInformation("Starting to load all enabled plugins...");

            var allPlugins = await GetAllPluginInfoAsync();
            var enabledPlugins = allPlugins.Where(p => p.IsEnabled).ToList();

            _logger.LogInformation("Found {TotalCount} plugins, {EnabledCount} enabled", 
                allPlugins.Count, enabledPlugins.Count);

            foreach (var pluginInfo in enabledPlugins)
            {
                try
                {
                    _logger.LogDebug("Loading plugin: {PluginId}", pluginInfo.PluginId);
                    
                    var plugin = await LoadPluginAsync(pluginInfo);
                    if (plugin != null)
                    {
                        pluginInfo.IsLoaded = true;
                        loadedPlugins.Add(pluginInfo);
                        _logger.LogDebug("Successfully loaded plugin: {PluginId}", pluginInfo.PluginId);
                    }
                    else
                    {
                        pluginInfo.IsLoaded = false;
                        failedPlugins.Add(pluginInfo);
                        _logger.LogWarning("Failed to load plugin: {PluginId}", pluginInfo.PluginId);
                    }
                }
                catch (Exception ex)
                {
                    pluginInfo.IsLoaded = false;
                    pluginInfo.LoadError = ex.Message;
                    failedPlugins.Add(pluginInfo);
                    _logger.LogError(ex, "Error loading plugin: {PluginId}", pluginInfo.PluginId);
                }
            }

            var totalLoadTime = DateTime.UtcNow - startTime;
            
            _logger.LogInformation("Plugin loading completed. {SuccessCount} loaded, {FailedCount} failed in {Duration}ms",
                loadedPlugins.Count, failedPlugins.Count, totalLoadTime.TotalMilliseconds);

            var eventArgs = new AllPluginsLoadedEventArgs(
                loadedPlugins.AsReadOnly(),
                failedPlugins.AsReadOnly(),
                totalLoadTime);

            AllPluginsLoaded?.Invoke(this, eventArgs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error during plugin loading");
            throw;
        }
    }


    public async Task<List<PluginInfo>> DiscoverPluginsAsync()
    {
        var plugins = new List<PluginInfo>();

        if (!Directory.Exists(_pluginsDirectory))
        {
            _logger.LogWarning("Plugins directory does not exist: {PluginsDirectory}", _pluginsDirectory);
            return plugins;
        }

        await _registryService.CleanupMissingPluginsAsync();

        var pluginDirectories = Directory.GetDirectories(_pluginsDirectory);

        foreach (var pluginDir in pluginDirectories)
        {
            try
            {
                var pluginInfo = await AnalyzePluginDirectoryAsync(pluginDir);
                if (pluginInfo != null)
                {
                    var registry = await _registryService.GetRegistryAsync();
                    var isNewPlugin = !registry.Plugins.ContainsKey(pluginInfo.PluginId);

                    await _registryService.RegisterPluginAsync(pluginInfo);
                
                    var integrityStatus = await _registryService.VerifyPluginIntegrityAsync(pluginInfo.PluginId);
                    if (integrityStatus == PluginIntegrityStatus.Modified)
                    {
                        _logger.LogWarning("Plugin {PluginId} has been modified since last run", pluginInfo.PluginId);
                    }

                    plugins.Add(pluginInfo);
                    
                    var eventArgs = new PluginDiscoveredEventArgs(pluginInfo, plugins.Count, isNewPlugin);
                    PluginDiscovered?.Invoke(this, eventArgs);

                    _logger.LogDebug("Plugin discovered event fired for {PluginId} (IsNew: {IsNew})", 
                        pluginInfo.PluginId, isNewPlugin);
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

    public async Task<List<PluginInfo>> GetAllPluginInfoAsync()
    {
        var discoveredPlugins = await DiscoverPluginsAsync();

        foreach (var plugin in discoveredPlugins)
        {
            _logger.LogDebug("Plugin {PluginId} discovered, loading settings", plugin.PluginId);

            try
            {
                var pluginSettings = await GetPluginSettingsAsync(plugin.PluginDirectory);
                
                plugin.IsEnabled = pluginSettings.IsEnabled;
                plugin.Configuration = pluginSettings.Configuration;
                
                _logger.LogDebug("Plugin {PluginId} loaded from settings: IsEnabled={IsEnabled}", 
                    plugin.PluginId, plugin.IsEnabled);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load settings for {PluginId}, defaulting to disabled", plugin.PluginId);
                
                plugin.IsEnabled = false;
                plugin.Configuration = plugin.Configuration ?? new Dictionary<string, object>();
                
                await EnsurePluginSettingsFileExistsAsync(plugin.PluginDirectory);
            }
        }

        return discoveredPlugins;
    }

    public async Task SetPluginEnabledAsync(string pluginId, bool enabled)
    {
        var plugins = await DiscoverPluginsAsync();
        var plugin = plugins.FirstOrDefault(p => p.PluginId == pluginId);
        
        if (plugin == null)
        {
            throw new InvalidOperationException($"Plugin {pluginId} not found");
        }

        var settings = await GetPluginSettingsAsync(plugin.PluginDirectory);
        settings.IsEnabled = enabled;
        settings.LastUpdated = DateTime.UtcNow;
        
        await SavePluginSettingsAsync(plugin.PluginDirectory, settings);
        
        _logger.LogInformation("Plugin {PluginId} {Status} in settings", pluginId, enabled ? "enabled" : "disabled");
    }

    public async Task UpdatePluginConfigurationAsync(string pluginId, Dictionary<string, object> configuration)
    {
        var plugins = await DiscoverPluginsAsync();
        var plugin = plugins.FirstOrDefault(p => p.PluginId == pluginId);
        
        if (plugin == null)
        {
            throw new InvalidOperationException($"Plugin {pluginId} not found");
        }

        var settings = await GetPluginSettingsAsync(plugin.PluginDirectory);
        settings.Configuration = configuration;
        settings.LastUpdated = DateTime.UtcNow;
        
        await SavePluginSettingsAsync(plugin.PluginDirectory, settings);
        
        _logger.LogDebug("Updated configuration for plugin {PluginId} in settings file", pluginId);
    }

    public async Task<PluginSettings> GetPluginSettingsAsync(string pluginDirectory)
    {
        var settingsPath = Path.Combine(pluginDirectory, "plugin-settings.json");
        
        try
        {
            if (!File.Exists(settingsPath))
            {
                _logger.LogDebug("No settings file found for {PluginDirectory}, creating default", pluginDirectory);
                return await CreateDefaultSettingsAsync(pluginDirectory);
            }

            var json = await File.ReadAllTextAsync(settingsPath);
            var settings = JsonSerializer.Deserialize<PluginSettings>(json, _jsonOptions);
            
            if (settings == null)
            {
                _logger.LogWarning("Failed to deserialize settings for {PluginDirectory}, creating default", pluginDirectory);
                return await CreateDefaultSettingsAsync(pluginDirectory);
            }

            var currentPlugin = await GetPluginInfoFromDirectoryAsync(pluginDirectory);
            if (currentPlugin != null)
            {
                settings = await MigrateSettingsIfNeededAsync(settings, currentPlugin, pluginDirectory);
            }

            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading plugin settings from {PluginDirectory}", pluginDirectory);
            return await CreateDefaultSettingsAsync(pluginDirectory);
        }
    }

    public async Task SavePluginSettingsAsync(string pluginDirectory, PluginSettings settings)
    {
        var settingsPath = Path.Combine(pluginDirectory, "plugin-settings.json");
        
        try
        {
            settings.LastUpdated = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            await File.WriteAllTextAsync(settingsPath, json);
            
            _logger.LogDebug("Saved plugin settings to {SettingsPath}", settingsPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save plugin settings to {SettingsPath}", settingsPath);
        }
    }

    public async Task<bool> HasConfigurableSettingsAsync(string pluginDirectory)
    {
        try
        {
            var pluginJsonPath = Path.Combine(pluginDirectory, "plugin.json");
            if (!File.Exists(pluginJsonPath))
            {
                _logger.LogDebug("No plugin.json found in {PluginDirectory}, no configurable settings", pluginDirectory);
                return false;
            }

            var json = await File.ReadAllTextAsync(pluginJsonPath);
            var pluginMetadata = JsonSerializer.Deserialize<PluginMetadata>(json, _jsonOptions);
            
            if (pluginMetadata?.Configuration == null)
            {
                return false;
            }

            if (pluginMetadata.Configuration.TryGetValue("schema", out var schemaObj))
            {
                string? schemaJson = null;
                
                if (schemaObj is JsonElement jsonElement)
                {
                    schemaJson = jsonElement.GetRawText();
                }
                else if (schemaObj is string schemaString)
                {
                    schemaJson = schemaString;
                }
                else
                {
                    schemaJson = JsonSerializer.Serialize(schemaObj, _jsonOptions);
                }

                if (!string.IsNullOrEmpty(schemaJson))
                {
                    using var document = JsonDocument.Parse(schemaJson);
                    var root = document.RootElement;
                    
                    if (root.TryGetProperty("properties", out var propertiesElement))
                    {
                        var hasProperties = propertiesElement.EnumerateObject().Any();
                        
                        if (hasProperties)
                        {
                            await EnsurePluginSettingsFileExistsAsync(pluginDirectory);
                        }
                        
                        return hasProperties;
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check configurable settings for {PluginDirectory}", pluginDirectory);
            return false;
        }
    }

    public async Task<bool> RollbackSettingsAsync(string pluginDirectory)
    {
        try
        {
            var currentSettings = await GetPluginSettingsAsync(pluginDirectory);
            
            if (currentSettings.PreviousConfiguration == null)
            {
                _logger.LogWarning("No previous configuration to rollback to for {PluginDirectory}", pluginDirectory);
                return false;
            }

            currentSettings.Configuration = currentSettings.PreviousConfiguration;
            currentSettings.SchemaVersion = currentSettings.PreviousSchemaVersion ?? "1.0.0";
            currentSettings.PreviousConfiguration = null;
            currentSettings.PreviousSchemaVersion = null;
            currentSettings.LastUpdated = DateTime.UtcNow;
            currentSettings.Metadata["RolledBackAt"] = DateTime.UtcNow.ToString("O");

            await SavePluginSettingsAsync(pluginDirectory, currentSettings);
            
            _logger.LogInformation("Successfully rolled back settings for {PluginDirectory}", pluginDirectory);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rollback settings for {PluginDirectory}", pluginDirectory);
            return false;
        }
    }

    public async Task<bool> ValidateSettingsSchemaAsync(string pluginDirectory)
    {
        try
        {
            var pluginInfo = await GetPluginInfoFromDirectoryAsync(pluginDirectory);
            if (pluginInfo == null)
            {
                return false;
            }

            var settings = await GetPluginSettingsAsync(pluginDirectory);
            var schema = await GetPluginSchemaAsync(pluginInfo);

            if (schema == null)
            {
                return true;
            }

            if (schema.RootElement.TryGetProperty("properties", out var properties))
            {
                foreach (var property in properties.EnumerateObject())
                {
                    var propertyName = property.Name;
                    var propertySchema = property.Value;
                    
                    if (schema.RootElement.TryGetProperty("required", out var requiredArray) &&
                        requiredArray.EnumerateArray().Any(req => req.GetString() == propertyName))
                    {
                        if (!settings.Configuration.ContainsKey(propertyName))
                        {
                            _logger.LogWarning("Required property {PropertyName} missing in settings for {PluginDirectory}", 
                                propertyName, pluginDirectory);
                            return false;
                        }
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate settings schema for {PluginDirectory}", pluginDirectory);
            return false;
        }
    }

    public async Task<IModPlugin?> LoadPluginAsync(PluginInfo pluginInfo)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            var integrityStatus = await _registryService.VerifyPluginIntegrityAsync(pluginInfo.PluginId);
            if (integrityStatus == PluginIntegrityStatus.Missing)
            {
                throw new FileNotFoundException($"Plugin assembly not found: {pluginInfo.AssemblyPath}");
            }
            if (integrityStatus == PluginIntegrityStatus.Corrupted)
            {
                throw new InvalidOperationException($"Plugin assembly is corrupted: {pluginInfo.PluginId}");
            }

            _logger.LogDebug("Loading plugin: {PluginId} from {AssemblyPath} (integrity: {Status})", 
                pluginInfo.PluginId, pluginInfo.AssemblyPath, integrityStatus);

            var loaderLogger = NullLogger<IsolatedPluginLoader>.Instance;
            using var loader = new IsolatedPluginLoader(loaderLogger, pluginInfo.PluginDirectory);
            var plugin = await loader.LoadPluginAsync(pluginInfo.AssemblyPath, pluginInfo.TypeName);

            if (plugin != null)
            {
                plugin.PluginDirectory = pluginInfo.PluginDirectory;
                plugin.IsEnabled = pluginInfo.IsEnabled;
                
                var pluginSettings = await GetPluginSettingsAsync(pluginInfo.PluginDirectory);
                var actualConfiguration = pluginSettings.Configuration;
                
                _logger.LogDebug("Initializing plugin {PluginId} with settings configuration: {ConfigKeys}", 
                    pluginInfo.PluginId, string.Join(", ", actualConfiguration.Keys));
                
                await plugin.InitializeAsync(actualConfiguration);
                
                var runtime = DateTime.UtcNow - startTime;
                await _registryService.RecordPluginLoadAsync(pluginInfo.PluginId, true, null, runtime);
                
                _logger.LogInformation("Successfully loaded plugin: {PluginId} in {Runtime}ms", 
                    pluginInfo.PluginId, runtime.TotalMilliseconds);
            }

            return plugin;
        }
        catch (Exception ex)
        {
            var runtime = DateTime.UtcNow - startTime;
            var errorMessage = $"Failed to load plugin {pluginInfo.PluginId}: {ex.Message}";
            _logger.LogError(ex, errorMessage);
        
            await _registryService.RecordPluginLoadAsync(pluginInfo.PluginId, false, errorMessage, runtime);
        
            pluginInfo.LoadError = errorMessage;
            pluginInfo.IsLoaded = false;
        
            return null;
        }
    }

    private async Task<PluginSettings> MigrateSettingsIfNeededAsync(
        PluginSettings existingSettings, 
        PluginInfo currentPlugin, 
        string pluginDirectory)
    {
        try
        {
            var needsMigration = existingSettings.Version != currentPlugin.Version ||
                                 existingSettings.SchemaVersion != GetExpectedSchemaVersion(currentPlugin);

            if (!needsMigration)
            {
                _logger.LogDebug("No migration needed for plugin {PluginId}", currentPlugin.PluginId);
                return existingSettings;
            }

            _logger.LogInformation("Migrating settings for plugin {PluginId} from version {OldVersion} to {NewVersion}",
                currentPlugin.PluginId, existingSettings.Version, currentPlugin.Version);

            existingSettings.PreviousConfiguration = new Dictionary<string, object>(existingSettings.Configuration);
            existingSettings.PreviousSchemaVersion = existingSettings.SchemaVersion;

            var expectedSchema = await GetPluginSchemaAsync(currentPlugin);
            
            var migratedConfig = await MigrateConfigurationAsync(
                existingSettings.Configuration, 
                expectedSchema, 
                currentPlugin);

            existingSettings.Configuration = migratedConfig;
            existingSettings.Version = currentPlugin.Version;
            existingSettings.SchemaVersion = GetExpectedSchemaVersion(currentPlugin);
            existingSettings.LastUpdated = DateTime.UtcNow;
            existingSettings.Metadata["MigratedAt"] = DateTime.UtcNow.ToString("O");
            existingSettings.Metadata["MigratedFrom"] = existingSettings.PreviousSchemaVersion;

            await SavePluginSettingsAsync(pluginDirectory, existingSettings);

            _logger.LogInformation("Successfully migrated settings for plugin {PluginId}", currentPlugin.PluginId);
            return existingSettings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate settings for plugin {PluginId}, using defaults", currentPlugin.PluginId);
            return await CreateDefaultSettingsAsync(pluginDirectory);
        }
    }

    private async Task<Dictionary<string, object>> MigrateConfigurationAsync(
        Dictionary<string, object> oldConfig,
        JsonDocument? expectedSchema,
        PluginInfo currentPlugin)
    {
        var migratedConfig = new Dictionary<string, object>();

        if (expectedSchema?.RootElement.TryGetProperty("properties", out var properties) == true)
        {
            foreach (var property in properties.EnumerateObject())
            {
                var propertyName = property.Name;
                var propertySchema = property.Value;

                if (oldConfig.TryGetValue(propertyName, out var oldValue))
                {
                    try
                    {
                        var migratedValue = await MigratePropertyValueAsync(oldValue, propertySchema);
                        migratedConfig[propertyName] = migratedValue;
                        
                        _logger.LogDebug("Migrated property {PropertyName} for plugin {PluginId}", 
                            propertyName, currentPlugin.PluginId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to migrate property {PropertyName}, using default", propertyName);
                        
                        if (propertySchema.TryGetProperty("default", out var defaultElement))
                        {
                            migratedConfig[propertyName] = JsonElementToObject(defaultElement);
                        }
                    }
                }
                else
                {
                    if (propertySchema.TryGetProperty("default", out var defaultElement))
                    {
                        migratedConfig[propertyName] = JsonElementToObject(defaultElement);
                        _logger.LogDebug("Added new property {PropertyName} with default value for plugin {PluginId}", 
                            propertyName, currentPlugin.PluginId);
                    }
                }
            }
        }

        return migratedConfig;
    }

    private async Task<object> MigratePropertyValueAsync(object oldValue, JsonElement propertySchema)
    {
        if (!propertySchema.TryGetProperty("type", out var typeElement))
        {
            return oldValue;
        }

        var expectedType = typeElement.GetString();
        
        return expectedType switch
        {
            "string" => oldValue?.ToString() ?? string.Empty,
            "boolean" => ConvertToBoolean(oldValue),
            "integer" => ConvertToInteger(oldValue),
            "number" => ConvertToNumber(oldValue),
            "array" => ConvertToArray(oldValue),
            "object" => ConvertToObject(oldValue),
            _ => oldValue
        };
    }

    private bool ConvertToBoolean(object? value)
    {
        return value switch
        {
            bool b => b,
            string s => bool.TryParse(s, out var result) && result,
            int i => i != 0,
            _ => false
        };
    }

    private int ConvertToInteger(object? value)
    {
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, out var result) => result,
            _ => 0
        };
    }

    private double ConvertToNumber(object? value)
    {
        return value switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            string s when double.TryParse(s, out var result) => result,
            _ => 0.0
        };
    }

    private object ConvertToArray(object? value)
    {
        if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Select(JsonElementToObject).ToList();
        }
        
        return value is IEnumerable<object> enumerable ? enumerable.ToList() : new List<object>();
    }

    private object ConvertToObject(object? value)
    {
        if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            var objectDict = new Dictionary<string, object>();
            foreach (var property in element.EnumerateObject())
            {
                objectDict[property.Name] = JsonElementToObject(property.Value);
            }
            return objectDict;
        }
        
        return value is Dictionary<string, object> existingDict ? existingDict : new Dictionary<string, object>();
    }

    private object JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.TryGetInt32(out var i) ? i : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToObject).ToList(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
            _ => element.ToString()
        };
    }

    private string GetExpectedSchemaVersion(PluginInfo plugin)
    {
        return plugin.Version;
    }

    private async Task<JsonDocument?> GetPluginSchemaAsync(PluginInfo plugin)
    {
        try
        {
            if (plugin.Configuration.TryGetValue("schema", out var schemaObj))
            {
                string? schemaJson = null;
                
                if (schemaObj is JsonElement jsonElement)
                {
                    schemaJson = jsonElement.GetRawText();
                }
                else if (schemaObj is string schemaString)
                {
                    schemaJson = schemaString;
                }
                else
                {
                    schemaJson = JsonSerializer.Serialize(schemaObj, _jsonOptions);
                }

                if (!string.IsNullOrEmpty(schemaJson))
                {
                    return JsonDocument.Parse(schemaJson);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get plugin schema for {PluginId}", plugin.PluginId);
        }

        return null;
    }

    private async Task<PluginInfo?> GetPluginInfoFromDirectoryAsync(string pluginDirectory)
    {
        try
        {
            var pluginJsonPath = Path.Combine(pluginDirectory, "plugin.json");
            if (File.Exists(pluginJsonPath))
            {
                return await LoadPluginFromJsonAsync(pluginJsonPath, pluginDirectory);
            }

            return await AnalyzePluginDirectoryLegacyAsync(pluginDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get plugin info from {PluginDirectory}", pluginDirectory);
            return null;
        }
    }

    private async Task<PluginSettings> CreateDefaultSettingsAsync(string pluginDirectory)
    {
        try
        {
            var pluginInfo = await GetPluginInfoFromDirectoryAsync(pluginDirectory);
            
            var defaultSettings = new PluginSettings
            {
                IsEnabled = false,
                Configuration = pluginInfo?.Configuration ?? new Dictionary<string, object>(),
                Version = pluginInfo?.Version ?? "1.0.0",
                SchemaVersion = pluginInfo?.Version ?? "1.0.0",
                LastUpdated = DateTime.UtcNow
            };

            await SavePluginSettingsAsync(pluginDirectory, defaultSettings);
            return defaultSettings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create default settings for {PluginDirectory}", pluginDirectory);
            return new PluginSettings();
        }
    }

    private async Task EnsurePluginSettingsFileExistsAsync(string pluginDirectory)
    {
        var settingsPath = Path.Combine(pluginDirectory, "plugin-settings.json");
        
        if (!File.Exists(settingsPath))
        {
            var defaultSettings = new PluginSettings
            {
                IsEnabled = false,
                Configuration = new Dictionary<string, object>(),
                Version = "1.0.0",
                LastUpdated = DateTime.UtcNow
            };
            
            await SavePluginSettingsAsync(pluginDirectory, defaultSettings);
            _logger.LogDebug("Created default plugin-settings.json for plugin in {PluginDirectory}", pluginDirectory);
        }
    }

    private async Task<PluginInfo?> AnalyzePluginDirectoryAsync(string pluginDirectory)
    {
        try
        {
            var pluginJsonPath = Path.Combine(pluginDirectory, "plugin.json");
            if (File.Exists(pluginJsonPath))
            {
                return await LoadPluginFromJsonAsync(pluginJsonPath, pluginDirectory);
            }

            _logger.LogDebug("No plugin.json found in {PluginDirectory}, falling back to assembly scanning", pluginDirectory);
            return await AnalyzePluginDirectoryLegacyAsync(pluginDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze plugin directory: {PluginDirectory}", pluginDirectory);
            return null;
        }
    }
    
    private async Task<PluginInfo?> LoadPluginFromJsonAsync(string pluginJsonPath, string pluginDirectory)
    {
        try
        {
            _logger.LogDebug("Loading plugin metadata from {PluginJsonPath}", pluginJsonPath);
            
            var json = await File.ReadAllTextAsync(pluginJsonPath);
            var pluginMetadata = JsonSerializer.Deserialize<PluginMetadata>(json, _jsonOptions);
            
            if (pluginMetadata == null)
            {
                _logger.LogWarning("Failed to deserialize plugin.json from {PluginJsonPath}", pluginJsonPath);
                return null;
            }

            _logger.LogDebug("Deserialized plugin metadata: PluginId={PluginId}, AssemblyName={AssemblyName}, MainClass={MainClass}", 
                pluginMetadata.PluginId, pluginMetadata.AssemblyName, pluginMetadata.MainClass);

            if (string.IsNullOrWhiteSpace(pluginMetadata.PluginId) ||
                string.IsNullOrWhiteSpace(pluginMetadata.AssemblyName) ||
                string.IsNullOrWhiteSpace(pluginMetadata.MainClass))
            {
                _logger.LogWarning("Plugin metadata missing required fields in {PluginJsonPath}. PluginId='{PluginId}', AssemblyName='{AssemblyName}', MainClass='{MainClass}'", 
                    pluginJsonPath, pluginMetadata.PluginId, pluginMetadata.AssemblyName, pluginMetadata.MainClass);
                return null;
            }

            var assemblyPath = Path.Combine(pluginDirectory, pluginMetadata.AssemblyName);
            if (!File.Exists(assemblyPath))
            {
                _logger.LogWarning("Assembly file not found: {AssemblyPath}", assemblyPath);
                return null;
            }

            var fileInfo = new FileInfo(assemblyPath);
            
            var hasConfigurableSettings = await HasConfigurableSettingsAsync(pluginDirectory);
            
            var pluginInfo = new PluginInfo
            {
                PluginId = pluginMetadata.PluginId,
                DisplayName = pluginMetadata.DisplayName ?? pluginMetadata.PluginId,
                Description = pluginMetadata.Description ?? string.Empty,
                Version = pluginMetadata.Version ?? "1.0.0",
                Author = pluginMetadata.Author ?? "Unknown",
                AssemblyPath = assemblyPath,
                TypeName = pluginMetadata.MainClass,
                PluginDirectory = pluginDirectory,
                LastModified = fileInfo.LastWriteTime,
                IsLoaded = false,
                IsEnabled = false,
                HasConfigurableSettings = hasConfigurableSettings,
                Configuration = pluginMetadata.Configuration ?? new Dictionary<string, object>()
            };

            _logger.LogDebug("Created PluginInfo for {PluginId} with HasConfigurableSettings={HasConfigurableSettings}", 
                pluginInfo.PluginId, pluginInfo.HasConfigurableSettings);

            _logger.LogInformation("Successfully loaded plugin metadata: {PluginId} v{Version} by {Author}", 
                pluginInfo.PluginId, pluginInfo.Version, pluginInfo.Author);

            return pluginInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load plugin from JSON: {PluginJsonPath}", pluginJsonPath);
            return null;
        }
    }

    private async Task<PluginInfo?> AnalyzePluginDirectoryLegacyAsync(string pluginDirectory)
    {
        try
        {
            var dllFiles = Directory.GetFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                .Where(dll => !Path.GetFileName(dll).Equals("PluginManager.Core.dll", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            
            if (!dllFiles.Any())
            {
                _logger.LogDebug("No plugin DLL files found in plugin directory: {PluginDirectory}", pluginDirectory);
                return null;
            }

            foreach (var dllFile in dllFiles)
            {
                try
                {
                    var pluginInfo = await TryDiscoverPluginFromAssemblyAsync(dllFile, pluginDirectory);
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
            _logger.LogError(ex, "Failed to analyze plugin directory (legacy): {PluginDirectory}", pluginDirectory);
            return null;
        }
    }

    private async Task<PluginInfo?> TryDiscoverPluginFromAssemblyAsync(string dllFile, string pluginDirectory)
    {
        try
        {
            _logger.LogDebug("Attempting to discover plugin from assembly: {DllFile}", dllFile);
            
            var loaderLogger = NullLogger<IsolatedPluginLoader>.Instance;
            using var loader = new IsolatedPluginLoader(loaderLogger, pluginDirectory);
            
            var assembly = Assembly.LoadFrom(dllFile);
            var pluginTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(IModPlugin).IsAssignableFrom(t))
                .ToList();

            if (!pluginTypes.Any())
                return null;
            
            var pluginType = pluginTypes.First();
            
            var tempInstance = await loader.LoadPluginAsync(dllFile, pluginType.FullName!);
            if (tempInstance == null)
                return null;

            var fileInfo = new FileInfo(dllFile);
            
            var hasConfigurableSettings = await HasConfigurableSettingsAsync(pluginDirectory);
            
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
                IsLoaded = false,
                IsEnabled = false,
                HasConfigurableSettings = hasConfigurableSettings
            };
            
            await tempInstance.DisposeAsync();

            _logger.LogInformation("Successfully discovered plugin via assembly scanning: {PluginId}", pluginInfo.PluginId);
            return pluginInfo;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to discover plugin from assembly: {DllFile}", dllFile);
            return null;
        }
    }
}