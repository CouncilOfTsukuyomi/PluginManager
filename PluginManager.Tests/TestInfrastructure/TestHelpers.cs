using Microsoft.Extensions.Logging;
using PluginManager.Core.Models;

namespace PluginManager.Tests.TestInfrastructure;

public static class TestHelpers
{
    public static TestLogger<T> CreateTestLogger<T>() => new();

    public static TestLoggerProvider CreateLoggerProvider() => new();

    public static ILogger<T> CreateMockLogger<T>() => new TestLogger<T>();

    public static PluginInfo CreateTestPluginInfo(string pluginId = "test-plugin")
    {
        return new PluginInfo
        {
            PluginId = pluginId,
            DisplayName = "Test Plugin",
            Description = "A test plugin",
            Version = "1.0.0",
            Author = "Test Author",
            AssemblyPath = Path.Combine(AppContext.BaseDirectory, "TestPlugin.dll"),
            TypeName = "TestPlugin.TestPluginClass",
            PluginDirectory = Path.Combine(Path.GetTempPath(), "test-plugins", pluginId),
            IsEnabled = true,
            Configuration = new Dictionary<string, object>
            {
                { "testKey", "testValue" },
                { "timeout", 30000 }
            }
        };
    }

    public static PluginMod CreateTestMod(string name = "Test Mod")
    {
        return new PluginMod
        {
            Name = name,
            ModUrl = "https://example.com/mod/1",
            DownloadUrl = "https://example.com/download/1",
            ImageUrl = "https://example.com/image/1.jpg",
            Publisher = "Test Publisher",
            Type = "Equipment",
            UploadDate = DateTime.UtcNow.AddDays(-1),
            FileSize = 1024 * 1024, // 1MB
            PluginSource = "test-plugin"
        };
    }

    public static string CreateTempDirectory(string? prefix = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), prefix ?? "plugin-test", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    public static void CleanupTempDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}