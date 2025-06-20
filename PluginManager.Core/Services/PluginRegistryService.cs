using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PluginManager.Core.Enums;
using PluginManager.Core.Models;

namespace PluginManager.Core.Services;

public class PluginRegistryService : IDisposable
{
    private readonly ILogger<PluginRegistryService> _logger;
    private readonly string _registryPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private PluginRegistry _registry;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    public PluginRegistryService(ILogger<PluginRegistryService> logger, string pluginsBasePath)
    {
        _logger = logger;
        _registryPath = Path.Combine(pluginsBasePath, "plugin-registry.json");
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        _registry = new PluginRegistry();
    }

    public async Task InitializeAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            await LoadRegistryAsync();
            _logger.LogInformation("Plugin registry initialized with {Count} plugins", _registry.Plugins.Count);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<PluginRegistry> GetRegistryAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            return _registry;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task RegisterPluginAsync(PluginInfo pluginInfo)
    {
        await _semaphore.WaitAsync();
        try
        {
            if (_registry.Plugins.TryGetValue(pluginInfo.PluginId, out var existingEntry))
            {
                // Plugin already exists - only update discovery metadata
                var fileInfo = new FileInfo(pluginInfo.AssemblyPath);
                var hash = await ComputeFileHashAsync(pluginInfo.AssemblyPath);
                
                // Update only discovery/integrity metadata
                existingEntry.DisplayName = pluginInfo.DisplayName;
                existingEntry.Version = pluginInfo.Version;
                existingEntry.AssemblyPath = pluginInfo.AssemblyPath;
                existingEntry.AssemblyHash = hash;
                existingEntry.LastModified = fileInfo.LastWriteTime;
                existingEntry.AssemblySize = fileInfo.Length;
                
                _registry.LastUpdated = DateTime.UtcNow;
                await SaveRegistryAsync();
                
                _logger.LogDebug("Updated existing plugin {PluginId} discovery metadata", pluginInfo.PluginId);
            }
            else
            {
                // New plugin - create fresh entry (discovery metadata only)
                var entry = await CreateRegistryEntryAsync(pluginInfo);
                _registry.Plugins[pluginInfo.PluginId] = entry;
                _registry.LastUpdated = DateTime.UtcNow;
            
                await SaveRegistryAsync();
            
                _logger.LogInformation("Registered new plugin {PluginId} with hash {Hash}", 
                    pluginInfo.PluginId, entry.AssemblyHash[..8]);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<PluginIntegrityStatus> VerifyPluginIntegrityAsync(string pluginId)
    {
        await _semaphore.WaitAsync();
        try
        {
            if (!_registry.Plugins.TryGetValue(pluginId, out var entry))
            {
                return PluginIntegrityStatus.Missing;
            }

            // Check if assembly file exists
            if (!File.Exists(entry.AssemblyPath))
            {
                entry.IntegrityStatus = PluginIntegrityStatus.Missing;
                await SaveRegistryAsync();
                return PluginIntegrityStatus.Missing;
            }

            // Check file size
            var fileInfo = new FileInfo(entry.AssemblyPath);
            if (fileInfo.Length != entry.AssemblySize)
            {
                entry.IntegrityStatus = PluginIntegrityStatus.Modified;
                await SaveRegistryAsync();
                return PluginIntegrityStatus.Modified;
            }

            // Verify hash
            var currentHash = await ComputeFileHashAsync(entry.AssemblyPath);
            if (currentHash != entry.AssemblyHash)
            {
                entry.IntegrityStatus = PluginIntegrityStatus.Modified;
                entry.AssemblyHash = currentHash; // Update with new hash
                entry.LastModified = fileInfo.LastWriteTime;
                await SaveRegistryAsync();
                
                _logger.LogWarning("Plugin {PluginId} assembly has been modified. Hash changed from {OldHash} to {NewHash}", 
                    pluginId, entry.AssemblyHash[..8], currentHash[..8]);
                
                return PluginIntegrityStatus.Modified;
            }

            entry.IntegrityStatus = PluginIntegrityStatus.Valid;
            await SaveRegistryAsync();
            return PluginIntegrityStatus.Valid;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task RecordPluginLoadAsync(string pluginId, bool success, string? error = null, TimeSpan? runtime = null)
    {
        await _semaphore.WaitAsync();
        try
        {
            if (_registry.Plugins.TryGetValue(pluginId, out var entry))
            {
                entry.LastLoaded = DateTime.UtcNow;
                entry.LoadCount++;
                
                if (runtime.HasValue)
                {
                    entry.TotalRuntime += runtime.Value;
                }

                if (success)
                {
                    entry.LastError = null;
                }
                else
                {
                    entry.LastError = error;
                }

                await SaveRegistryAsync();
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<Dictionary<string, PluginIntegrityStatus>> VerifyAllPluginsAsync()
    {
        var results = new Dictionary<string, PluginIntegrityStatus>();
        
        foreach (var pluginId in _registry.Plugins.Keys)
        {
            var status = await VerifyPluginIntegrityAsync(pluginId);
            results[pluginId] = status;
        }

        return results;
    }

    public async Task CleanupMissingPluginsAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            var toRemove = new List<string>();
            
            foreach (var kvp in _registry.Plugins)
            {
                if (!File.Exists(kvp.Value.AssemblyPath))
                {
                    toRemove.Add(kvp.Key);
                    _logger.LogInformation("Removing missing plugin from registry: {PluginId}", kvp.Key);
                }
            }

            foreach (var pluginId in toRemove)
            {
                _registry.Plugins.Remove(pluginId);
            }

            if (toRemove.Any())
            {
                _registry.LastUpdated = DateTime.UtcNow;
                await SaveRegistryAsync();
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<PluginRegistryEntry> CreateRegistryEntryAsync(PluginInfo pluginInfo)
    {
        var fileInfo = new FileInfo(pluginInfo.AssemblyPath);
        var hash = await ComputeFileHashAsync(pluginInfo.AssemblyPath);

        return new PluginRegistryEntry
        {
            PluginId = pluginInfo.PluginId,
            DisplayName = pluginInfo.DisplayName,
            Version = pluginInfo.Version,
            AssemblyPath = pluginInfo.AssemblyPath,
            AssemblyHash = hash,
            LastModified = fileInfo.LastWriteTime,
            AssemblySize = fileInfo.Length,
            IntegrityStatus = PluginIntegrityStatus.Valid,
            LoadCount = 0,
            TotalRuntime = TimeSpan.Zero
        };
    }

    private async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hashBytes = await Task.Run(() => sha256.ComputeHash(stream));
        return Convert.ToHexString(hashBytes);
    }

    private async Task LoadRegistryAsync()
    {
        try
        {
            if (!File.Exists(_registryPath))
            {
                _registry = new PluginRegistry();
                await SaveRegistryAsync();
                return;
            }

            var json = await File.ReadAllTextAsync(_registryPath);
            _registry = JsonSerializer.Deserialize<PluginRegistry>(json, _jsonOptions) ?? new PluginRegistry();
            
            _logger.LogDebug("Loaded plugin registry with {Count} plugins", _registry.Plugins.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load plugin registry, creating new one");
            _registry = new PluginRegistry();
            await SaveRegistryAsync();
        }
    }

    private async Task SaveRegistryAsync()
    {
        try
        {
            _registry.LastUpdated = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(_registry, _jsonOptions);
            await File.WriteAllTextAsync(_registryPath, json);
            
            _logger.LogDebug("Saved plugin registry with {Count} plugins", _registry.Plugins.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save plugin registry");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _semaphore.Dispose();
        _disposed = true;
    }
}