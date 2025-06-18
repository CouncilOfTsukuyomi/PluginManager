
using Microsoft.Extensions.Logging;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;

namespace PluginManager.Core.Security;

public class SecurityPluginProxy : IModPlugin
{
    private readonly IModPlugin _innerPlugin;
    private readonly ILogger _logger;
    private readonly SecurityPolicy _policy;
    private readonly Dictionary<string, int> _methodCallCounts = new();
    private readonly DateTime _createdAt = DateTime.UtcNow;

    public string PluginId => _innerPlugin.PluginId;
    public string DisplayName => _innerPlugin.DisplayName;
    public string Description => _innerPlugin.Description;
    public string Version => _innerPlugin.Version;
    public string Author => _innerPlugin.Author;
    public bool IsEnabled 
    { 
        get => _innerPlugin.IsEnabled; 
        set => _innerPlugin.IsEnabled = value; 
    }
    public string PluginDirectory 
    { 
        get => _innerPlugin.PluginDirectory; 
        set => _innerPlugin.PluginDirectory = ValidateDirectory(value); 
    }

    public SecurityPluginProxy(IModPlugin innerPlugin, ILogger logger)
    {
        _innerPlugin = innerPlugin;
        _logger = logger;
        _policy = SecurityPolicy.Default;
    }

    public async Task InitializeAsync(Dictionary<string, object> configuration)
    {
        if (!CheckMethodCallLimit(nameof(InitializeAsync)))
            throw new SecurityException("Method call limit exceeded");

        var sanitizedConfig = SanitizeConfiguration(configuration);
        
        using var timeout = new CancellationTokenSource(_policy.MethodTimeoutMs);
        try
        {
            await _innerPlugin.InitializeAsync(sanitizedConfig).WaitAsync(timeout.Token);
            _logger.LogDebug("Plugin {PluginId} initialized successfully", PluginId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Plugin {PluginId} initialization timed out", PluginId);
            throw new SecurityException("Plugin initialization timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plugin {PluginId} initialization failed", PluginId);
            throw;
        }
    }

    public async Task<List<PluginMod>> GetRecentModsAsync()
    {
        if (!CheckMethodCallLimit(nameof(GetRecentModsAsync)))
            throw new SecurityException("Method call limit exceeded");

        using var timeout = new CancellationTokenSource(_policy.MethodTimeoutMs);
        try
        {
            var mods = await _innerPlugin.GetRecentModsAsync().WaitAsync(timeout.Token);
            var validatedMods = ValidateMods(mods);
            
            _logger.LogDebug("Plugin {PluginId} returned {Count} validated mods", PluginId, validatedMods.Count);
            return validatedMods;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Plugin {PluginId} GetRecentMods operation timed out", PluginId);
            throw new SecurityException("GetRecentMods operation timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plugin {PluginId} GetRecentMods failed", PluginId);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(_policy.MethodTimeoutMs);
            await _innerPlugin.DisposeAsync().AsTask().WaitAsync(timeout.Token);
            _logger.LogDebug("Plugin {PluginId} disposed successfully", PluginId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Plugin {PluginId} dispose operation timed out", PluginId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing plugin {PluginId}", PluginId);
        }
    }

    private bool CheckMethodCallLimit(string methodName)
    {
        if (!_methodCallCounts.ContainsKey(methodName))
            _methodCallCounts[methodName] = 0;

        _methodCallCounts[methodName]++;

        var limit = _policy.MethodCallLimits.GetValueOrDefault(methodName, _policy.DefaultMethodCallLimit);
        
        if (_methodCallCounts[methodName] > limit)
        {
            _logger.LogWarning("Plugin {PluginId} exceeded call limit for {Method}: {Count}/{Limit}",
                PluginId, methodName, _methodCallCounts[methodName], limit);
            return false;
        }

        return true;
    }

    private Dictionary<string, object> SanitizeConfiguration(Dictionary<string, object> configuration)
    {
        var sanitized = new Dictionary<string, object>();

        foreach (var kvp in configuration)
        {
            if (_policy.AllowedConfigKeys.Contains(kvp.Key) || _policy.AllowAllConfigKeys)
            {
                sanitized[kvp.Key] = SanitizeValue(kvp.Value);
            }
            else
            {
                _logger.LogWarning("Blocked configuration key {Key} for plugin {PluginId}", 
                    kvp.Key, PluginId);
            }
        }

        return sanitized;
    }

    private object SanitizeValue(object value)
    {
        // Remove potentially dangerous values
        if (value is string str)
        {
            // Remove script tags, file paths, etc.
            str = str.Replace("<script", "", StringComparison.OrdinalIgnoreCase);
            str = str.Replace("javascript:", "", StringComparison.OrdinalIgnoreCase);
            str = str.Replace("file://", "", StringComparison.OrdinalIgnoreCase);
            
            // Limit string length
            if (str.Length > _policy.MaxStringLength)
                str = str[.._policy.MaxStringLength];
                
            return str;
        }

        return value;
    }

    private List<PluginMod> ValidateMods(List<PluginMod> mods)
    {
        if (mods.Count > _policy.MaxModsPerCall)
        {
            _logger.LogWarning("Plugin {PluginId} returned too many mods: {Count}/{Max}",
                PluginId, mods.Count, _policy.MaxModsPerCall);
            mods = mods.Take(_policy.MaxModsPerCall).ToList();
        }

        return mods.Select(ValidateMod).ToList();
    }

    private PluginMod ValidateMod(PluginMod mod)
    {
        // Validate URLs
        if (!IsValidUrl(mod.ModUrl))
        {
            _logger.LogWarning("Invalid ModUrl from plugin {PluginId}: {Url}", PluginId, mod.ModUrl);
            mod.ModUrl = "";
        }

        if (!IsValidUrl(mod.DownloadUrl))
        {
            _logger.LogWarning("Invalid DownloadUrl from plugin {PluginId}: {Url}", PluginId, mod.DownloadUrl);
            mod.DownloadUrl = "";
        }

        if (!IsValidUrl(mod.ImageUrl))
        {
            _logger.LogWarning("Invalid ImageUrl from plugin {PluginId}: {Url}", PluginId, mod.ImageUrl);
            mod.ImageUrl = "";
        }

        // Sanitize text fields
        mod.Name = SanitizeText(mod.Name);
        mod.Publisher = SanitizeText(mod.Publisher);
        mod.Type = SanitizeText(mod.Type);
        mod.Version = SanitizeText(mod.Version);

        return mod;
    }

    private bool IsValidUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return true;
        
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
            
        // Only allow HTTP and HTTPS
        if (uri.Scheme != "http" && uri.Scheme != "https")
            return false;
            
        // Block localhost and private IPs for security
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("127.0.0.1") ||
            uri.Host.StartsWith("192.168.") ||
            uri.Host.StartsWith("10.") ||
            uri.Host.StartsWith("172."))
        {
            _logger.LogWarning("Blocked private/localhost URL from plugin {PluginId}: {Url}", PluginId, url);
            return false;
        }
        
        return true;
    }

    private string SanitizeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        
        // Remove HTML tags
        text = System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", "");
        
        // Remove control characters
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[\x00-\x1F\x7F]", "");
        
        // Limit length
        if (text.Length > _policy.MaxStringLength)
            text = text[.._policy.MaxStringLength];
        
        return text.Trim();
    }

    private string ValidateDirectory(string directory)
    {
        if (string.IsNullOrEmpty(directory)) return directory;
        
        try
        {
            var fullPath = Path.GetFullPath(directory);
            var allowedBasePath = Path.GetFullPath(_policy.AllowedPluginBasePath);
            
            if (!fullPath.StartsWith(allowedBasePath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Plugin {PluginId} attempted to access directory outside allowed path: {Directory}", 
                    PluginId, directory);
                throw new SecurityException($"Plugin directory outside allowed path: {directory}");
            }
        }
        catch (Exception ex) when (!(ex is SecurityException))
        {
            _logger.LogError(ex, "Error validating directory path for plugin {PluginId}: {Directory}", 
                PluginId, directory);
            throw new SecurityException($"Invalid directory path: {directory}");
        }
        
        return directory;
    }
}