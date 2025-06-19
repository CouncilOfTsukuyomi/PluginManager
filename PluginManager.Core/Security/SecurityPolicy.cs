using PluginManager.Core.Interfaces;

namespace PluginManager.Core.Security;

public class SecurityPolicy
{
    public static SecurityPolicy Default => new()
    {
        MethodTimeoutMs = 30000, // 30 seconds
        DefaultMethodCallLimit = 100,
        MethodCallLimits = new Dictionary<string, int>
        {
            [nameof(IModPlugin.InitializeAsync)] = 3, // Allow retries
            [nameof(IModPlugin.GetRecentModsAsync)] = 50 // Reasonable limit for mod fetching
        },
        MaxModsPerCall = 200,
        MaxStringLength = 2000,
        AllowedConfigKeys = new HashSet<string> 
        { 
            "ApiKey", "BaseUrl", "Timeout", "EnableDebug", "CacheDuration",
            "UserAgent", "RequestDelay", "MaxRetries", "ProxyUrl"
        },
        AllowAllConfigKeys = false,
        AllowedPluginBasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins")
    };

    public int MethodTimeoutMs { get; set; }
    public int DefaultMethodCallLimit { get; set; }
    public Dictionary<string, int> MethodCallLimits { get; set; } = new();
    public int MaxModsPerCall { get; set; }
    public int MaxStringLength { get; set; }
    public HashSet<string> AllowedConfigKeys { get; set; } = new();
    public bool AllowAllConfigKeys { get; set; }
    public string AllowedPluginBasePath { get; set; } = string.Empty;
}

public class SecurityException : Exception
{
    public SecurityException(string message) : base(message) { }
    public SecurityException(string message, Exception innerException) : base(message, innerException) { }
}