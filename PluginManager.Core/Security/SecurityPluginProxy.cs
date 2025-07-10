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
    private readonly Dictionary<string, DateTime> _lastMethodCallReset = new();
    private readonly DateTime _createdAt = DateTime.UtcNow;
    private readonly TimeSpan _callLimitResetInterval = TimeSpan.FromMinutes(2);

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
        
        var timeoutMs = GetMethodTimeout(nameof(InitializeAsync));
        using var timeout = new CancellationTokenSource(timeoutMs);
        try
        {
            await _innerPlugin.InitializeAsync(sanitizedConfig).WaitAsync(timeout.Token);
            _logger.LogDebug("Plugin {PluginId} initialized successfully", PluginId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Plugin {PluginId} initialization timed out after {TimeoutMs}ms", PluginId, timeoutMs);
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

        var timeoutMs = GetMethodTimeout(nameof(GetRecentModsAsync));
        using var timeout = new CancellationTokenSource(timeoutMs);
        try
        {
            var mods = await _innerPlugin.GetRecentModsAsync().WaitAsync(timeout.Token);
            var validatedMods = ValidateMods(mods);
            
            _logger.LogDebug("Plugin {PluginId} returned {Count} validated mods", PluginId, validatedMods.Count);
            return validatedMods;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Plugin {PluginId} GetRecentMods operation timed out after {TimeoutMs}ms", PluginId, timeoutMs);
            throw new SecurityException("GetRecentMods operation timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plugin {PluginId} GetRecentMods failed", PluginId);
            throw;
        }
    }

    public void RequestCancellation()
    {
        try
        {
            _logger.LogDebug("Requesting cancellation for plugin {PluginId}", PluginId);
            _innerPlugin.RequestCancellation();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting cancellation for plugin {PluginId}", PluginId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            var timeoutMs = GetMethodTimeout(nameof(DisposeAsync));
            using var timeout = new CancellationTokenSource(timeoutMs);
            await _innerPlugin.DisposeAsync().AsTask().WaitAsync(timeout.Token);
            _logger.LogDebug("Plugin {PluginId} disposed successfully", PluginId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Plugin {PluginId} disposal timed out after", PluginId);
            throw new SecurityException("Plugin disposal timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing plugin {PluginId}", PluginId);
            throw;
        }
    }

    private int GetMethodTimeout(string methodName)
    {
        // Check if there's a specific timeout for this method
        if (_policy.MethodTimeouts.TryGetValue(methodName, out var specificTimeout))
        {
            return specificTimeout;
        }
        
        // Fall back to a default timeout (30 seconds)
        return 30000;
    }

    private bool CheckMethodCallLimit(string methodName)
    {
        var currentTime = DateTime.UtcNow;
        
        // Check if we need to reset the counter for this method
        if (_lastMethodCallReset.TryGetValue(methodName, out var lastReset))
        {
            if (currentTime - lastReset >= _callLimitResetInterval)
            {
                _methodCallCounts[methodName] = 0;
                _lastMethodCallReset[methodName] = currentTime;
                _logger.LogDebug("Reset method call count for {MethodName} in plugin {PluginId}", methodName, PluginId);
            }
        }
        else
        {
            // First time calling this method
            _lastMethodCallReset[methodName] = currentTime;
            _methodCallCounts[methodName] = 0;
        }

        _methodCallCounts[methodName]++;

        var limit = _policy.MethodCallLimits.GetValueOrDefault(methodName, _policy.DefaultMethodCallLimit);
        
        if (_methodCallCounts[methodName] > limit)
        {
            _logger.LogWarning("Plugin {PluginId} exceeded call limit for {MethodName}: {Count}/{Limit}", 
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
            if (_policy.AllowAllConfigKeys || _policy.AllowedConfigKeys.Contains(kvp.Key))
            {
                sanitized[kvp.Key] = SanitizeValue(kvp.Value);
            }
            else
            {
                _logger.LogWarning("Blocked configuration key: {Key}", kvp.Key);
            }
        }
        
        return sanitized;
    }

    private object SanitizeValue(object value)
    {
        return value switch
        {
            string str => SanitizeText(str),
            IEnumerable<object> enumerable => enumerable.Select(SanitizeValue).ToList(),
            _ => value
        };
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
        return new PluginMod
        {
            Name = SanitizeText(mod.Name),
            Publisher = SanitizeText(mod.Publisher),
            ImageUrl = IsValidUrl(mod.ImageUrl) ? mod.ImageUrl : string.Empty,
            ModUrl = IsValidUrl(mod.ModUrl) ? mod.ModUrl : string.Empty,
            DownloadUrl = IsValidUrl(mod.DownloadUrl) ? mod.DownloadUrl : string.Empty,
            PluginSource = SanitizeText(mod.PluginSource),
            UploadDate = mod.UploadDate,
        };
    }

    private bool IsValidUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && 
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private string SanitizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        if (text.Length > _policy.MaxStringLength)
        {
            text = text[.._policy.MaxStringLength];
        }

        return text;
    }

    private string ValidateDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return string.Empty;

        var normalizedPath = Path.GetFullPath(directory);
        var allowedPath = Path.GetFullPath(_policy.AllowedPluginBasePath);
        
        if (!normalizedPath.StartsWith(allowedPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException($"Plugin directory outside allowed path: {directory}");
        }

        return directory;
    }
}