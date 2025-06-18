
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;

namespace PluginManager.Tests.TestInfrastructure;

public class MockPlugin : IModPlugin
{
    public string PluginId { get; set; } = "test-plugin";
    public string DisplayName { get; set; } = "Test Plugin";
    public string Description { get; set; } = "A test plugin";
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = "Test Author";
    public bool IsEnabled { get; set; } = true;
    public string PluginDirectory { get; set; } = "";

    public bool InitializeCalled { get; private set; }
    public bool GetRecentModsCalled { get; private set; }
    public bool DisposeCalled { get; private set; }
    public Dictionary<string, object> LastConfiguration { get; private set; } = new();

    // Test configuration
    public TimeSpan InitializeDelay { get; set; } = TimeSpan.Zero;
    public TimeSpan GetRecentModsDelay { get; set; } = TimeSpan.Zero;
    public Exception? InitializeException { get; set; }
    public Exception? GetRecentModsException { get; set; }
    public List<PluginMod> ModsToReturn { get; set; } = new();

    public async Task InitializeAsync(Dictionary<string, object> configuration)
    {
        InitializeCalled = true;
        LastConfiguration = configuration;
        
        if (InitializeDelay > TimeSpan.Zero)
            await Task.Delay(InitializeDelay);
            
        if (InitializeException != null)
            throw InitializeException;
    }

    public async Task<List<PluginMod>> GetRecentModsAsync()
    {
        GetRecentModsCalled = true;
        
        if (GetRecentModsDelay > TimeSpan.Zero)
            await Task.Delay(GetRecentModsDelay);
            
        if (GetRecentModsException != null)
            throw GetRecentModsException;
            
        return ModsToReturn;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalled = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Plugin with constructor dependencies for testing dependency injection
/// </summary>
public class PluginWithDependencies : IModPlugin
{
    public string PluginId => "plugin-with-deps";
    public string DisplayName => "Plugin With Dependencies";
    public string Description => "Test plugin with constructor dependencies";
    public string Version => "1.0.0";
    public string Author => "Test";
    public bool IsEnabled { get; set; } = true;
    public string PluginDirectory { get; set; } = "";

    public HttpClient? HttpClient { get; }
    public string? ConfigDirectory { get; }
    public TimeSpan? Timeout { get; }

    public PluginWithDependencies(HttpClient httpClient, string configDirectory, TimeSpan timeout)
    {
        HttpClient = httpClient;
        ConfigDirectory = configDirectory;
        Timeout = timeout;
    }

    public Task InitializeAsync(Dictionary<string, object> configuration) => Task.CompletedTask;
    public Task<List<PluginMod>> GetRecentModsAsync() => Task.FromResult(new List<PluginMod>());
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Malicious plugin for security testing
/// </summary>
public class MaliciousPlugin : IModPlugin
{
    public string PluginId => "malicious-plugin";
    public string DisplayName => "Malicious Plugin";
    public string Description => "A plugin that tries to do bad things";
    public string Version => "1.0.0";
    public string Author => "Hacker";
    public bool IsEnabled { get; set; } = true;
    public string PluginDirectory { get; set; } = "";

    public async Task InitializeAsync(Dictionary<string, object> configuration)
    {
        // Try to access restricted directory
        if (configuration.ContainsKey("restricted_path"))
        {
            PluginDirectory = "/etc/passwd"; // Try to set to system directory
        }
    }

    public async Task<List<PluginMod>> GetRecentModsAsync()
    {
        // Return many mods to test limits
        var mods = new List<PluginMod>();
        for (int i = 0; i < 1000; i++)
        {
            mods.Add(new PluginMod
            {
                Name = $"<script>alert('xss')</script>Mod {i}", // XSS attempt
                ModUrl = "file:///etc/passwd", // File access attempt
                DownloadUrl = "javascript:alert('xss')", // JavaScript injection
                ImageUrl = "http://localhost:8080/admin", // Localhost access attempt
                Publisher = new string('A', 10000), // Very long string
                Type = "Malicious"
            });
        }
        return mods;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}