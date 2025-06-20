using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using MessagePack;
using Microsoft.Extensions.Logging;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;

namespace PluginManager.Core.Plugins;

/// <summary>
/// Base class for mod plugins with common caching functionality
/// </summary>
public abstract class BaseModPlugin : IModPlugin
{
    protected readonly ILogger Logger;
    protected readonly TimeSpan CacheDuration;
    private string? _lastConfigHash;
    private bool _disposed = false;

    public abstract string PluginId { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public abstract string Version { get; }
    public abstract string Author { get; }
    public bool IsEnabled { get; set; } = false;
    public string PluginDirectory { get; set; } = string.Empty;

    protected BaseModPlugin(ILogger logger, TimeSpan? cacheDuration = null)
    {
        Logger = logger;
        CacheDuration = cacheDuration ?? TimeSpan.FromMinutes(30);
        Logger.LogInformation("Plugin {PluginId} ({DisplayName}) v{Version} by {Author} initialized", 
            PluginId, DisplayName, Version, Author);
    }

    public abstract Task<List<PluginMod>> GetRecentModsAsync();
    public abstract Task InitializeAsync(Dictionary<string, object> configuration);

    public virtual async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            Logger.LogInformation("Plugin {PluginId} ({DisplayName}) is being disposed/disabled", 
                PluginId, DisplayName);
            
            try
            {
                // Perform any cleanup operations here
                await OnDisposingAsync();
                
                Logger.LogInformation("Plugin {PluginId} ({DisplayName}) cleanup completed successfully", 
                    PluginId, DisplayName);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error occurred while disposing plugin {PluginId} ({DisplayName})", 
                    PluginId, DisplayName);
            }
            finally
            {
                _disposed = true;
                Logger.LogInformation("Plugin {PluginId} ({DisplayName}) has been successfully disposed", 
                    PluginId, DisplayName);
            }
        }
    }

    /// <summary>
    /// Override this method in derived classes to perform custom cleanup
    /// </summary>
    protected virtual Task OnDisposingAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Finalizer - will be called by the garbage collector
    /// This ensures logging even if DisposeAsync is not called properly
    /// </summary>
    ~BaseModPlugin()
    {
        if (!_disposed)
        {
            // Note: Logger might not be available in finalizer, so we use a fallback
            try
            {
                Logger?.LogWarning("Plugin {PluginId} ({DisplayName}) is being finalized without proper disposal", 
                    PluginId, DisplayName);
            }
            catch
            {
                // If logging fails in finaliser, we can't do much
                // Consider using System.Diagnostics.Debug.WriteLine as fallback
                System.Diagnostics.Debug.WriteLine($"Plugin {PluginId} ({DisplayName}) finalized without proper disposal");
            }
        }
    }

    /// <summary>
    /// Get the cache file path for this plugin
    /// </summary>
    protected virtual string GetCacheFilePath()
    {
        return Path.Combine(PluginDirectory, "mods.cache");
    }

    /// <summary>
    /// Get the settings file path for this plugin
    /// </summary>
    protected virtual string GetSettingsFilePath()
    {
        return Path.Combine(PluginDirectory, "plugin-settings.json");
    }

    /// <summary>
    /// Load cached data from file
    /// </summary>
    protected virtual PluginCacheData? LoadCacheFromFile()
    {
        try
        {
            var cacheFile = GetCacheFilePath();
            if (!File.Exists(cacheFile))
                return null;

            var bytes = File.ReadAllBytes(cacheFile);
            var data = MessagePackSerializer.Deserialize<PluginCacheData>(bytes);
            
            return data.PluginId == PluginId ? data : null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load cache from file for plugin {PluginId}", PluginId);
            return null;
        }
    }

    /// <summary>
    /// Save cache data to file
    /// </summary>
    protected virtual void SaveCacheToFile(PluginCacheData data)
    {
        try
        {
            data.PluginId = PluginId;
            var bytes = MessagePackSerializer.Serialize(data);
            File.WriteAllBytes(GetCacheFilePath(), bytes);

            Logger.LogDebug("Cache saved for plugin {PluginId}, valid until {ExpirationTime}", 
                PluginId, data.ExpirationTime.ToString("u"));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to save cache to file for plugin {PluginId}", PluginId);
        }
    }

    /// <summary>
    /// Check if cache should be invalidated due to configuration changes
    /// </summary>
    protected virtual void InvalidateCacheOnConfigChange(string currentConfigHash)
    {
        if (currentConfigHash != _lastConfigHash)
        {
            Logger.LogDebug("Configuration changed for plugin {PluginId}. Invalidating cache.", PluginId);

            var cacheFile = GetCacheFilePath();
            if (File.Exists(cacheFile))
            {
                try
                {
                    File.Delete(cacheFile);
                    Logger.LogDebug("Cache file deleted for plugin {PluginId}", PluginId);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to delete cache file for plugin {PluginId}", PluginId);
                }
            }

            _lastConfigHash = currentConfigHash;
        }
    }

    /// <summary>
    /// Generate a hash for the current configuration
    /// </summary>
    protected virtual string GetConfigurationHash(Dictionary<string, object>? configuration = null)
    {
        if (configuration == null || !configuration.Any())
            return string.Empty;

        var configString = string.Join("|", configuration.OrderBy(kv => kv.Key)
            .Select(kv => $"{kv.Key}:{kv.Value}"));
        
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(configString));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Normalize mod names by removing HTML entities and non-ASCII characters
    /// </summary>
    protected virtual string NormalizeModName(string name)
    {
        var decoded = WebUtility.HtmlDecode(name);
        var asciiOnly = string.Concat(decoded.Where(c => c <= 127));
        var sanitized = asciiOnly
            .Replace("\\n", " ")
            .Replace("\\r", " ")
            .Replace("\\t", " ");

        var normalized = Regex
            .Replace(sanitized, "\\s+", " ")
            .Trim();

        return normalized;
    }

    /// <summary>
    /// Helper method for plugins to get download link for a mod
    /// </summary>
    protected abstract Task<string?> GetModDownloadLinkAsync(string modUrl);
}