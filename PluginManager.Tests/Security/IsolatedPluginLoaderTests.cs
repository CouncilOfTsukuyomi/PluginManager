using FluentAssertions;
using PluginManager.Core.Security;
using PluginManager.Tests.TestInfrastructure;
using Xunit;

namespace PluginManager.Tests.Security;

public class IsolatedPluginLoaderTests : IDisposable
{
    private readonly TestLogger<IsolatedPluginLoader> _logger;
    private readonly string _tempDir;

    public IsolatedPluginLoaderTests()
    {
        _logger = TestHelpers.CreateTestLogger<IsolatedPluginLoader>();
        _tempDir = TestHelpers.CreateTempDirectory();
    }

    public void Dispose()
    {
        TestHelpers.CleanupTempDirectory(_tempDir);
    }

    [Fact]
    public void Constructor_ShouldCreateLoader()
    {
        // Act
        using var loader = new IsolatedPluginLoader(_logger, _tempDir);

        // Assert
        loader.Should().NotBeNull();
    }

    [Fact]
    public async Task LoadPluginAsync_WithNonExistentAssembly_ShouldReturnNull()
    {
        // Arrange
        using var loader = new IsolatedPluginLoader(_logger, _tempDir);
        var assemblyPath = Path.Combine(_tempDir, "nonexistent.dll");

        // Act
        var plugin = await loader.LoadPluginAsync(assemblyPath, "TestPlugin");

        // Assert
        plugin.Should().BeNull();
        _logger.LogEntries.Should().Contain(entry => 
            entry.LogLevel == Microsoft.Extensions.Logging.LogLevel.Error &&
            entry.Message.Contains("Failed to load plugin in isolated context"));
    }

    [Fact]
    public async Task LoadPluginAsync_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var loader = new IsolatedPluginLoader(_logger, _tempDir);
        loader.Dispose();

        // Act & Assert
        var act = () => loader.LoadPluginAsync("assembly.dll", "Type");
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var loader = new IsolatedPluginLoader(_logger, _tempDir);

        // Act & Assert
        var act = () => loader.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var loader = new IsolatedPluginLoader(_logger, _tempDir);

        // Act & Assert
        loader.Dispose();
        var act = () => loader.Dispose();
        act.Should().NotThrow();
    }
}