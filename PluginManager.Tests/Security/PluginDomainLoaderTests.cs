using FluentAssertions;
using PluginManager.Core.Security;
using PluginManager.Tests.TestInfrastructure;
using Xunit;

namespace PluginManager.Tests.Security;

public class PluginDomainLoaderTests : IDisposable
{
    private readonly List<string> _tempDirectories = new();

    [Fact]
    public void LoadPlugin_WithNonExistentAssembly_ShouldReturnNull()
    {
        // Arrange
        using var loader = new PluginDomainLoader();
        var tempDir = CreateTempDirectory();

        // Act
        var plugin = loader.LoadPlugin("nonexistent.dll", "TestType", tempDir);

        // Assert
        plugin.Should().BeNull();
    }

    [Fact]
    public void LoadPlugin_WithInvalidAssemblyFile_ShouldReturnNull()
    {
        // Arrange
        using var loader = new PluginDomainLoader();
        var tempDir = CreateTempDirectory();

        // Create a dummy file that's not a valid assembly
        var assemblyPath = Path.Combine(tempDir, "test.dll");
        File.WriteAllBytes(assemblyPath, new byte[] { 0x4D, 0x5A }); // Invalid PE header

        // Act
        var plugin = loader.LoadPlugin(assemblyPath, "NonExistentType", tempDir);

        // Assert
        plugin.Should().BeNull();
    }

    [Fact]
    public void LoadPlugin_WithValidAssemblyButInvalidType_ShouldReturnNull()
    {
        // Arrange
        using var loader = new PluginDomainLoader();
        var tempDir = CreateTempDirectory();

        // Use the current assembly but with a non-existent type
        var assemblyPath = typeof(PluginDomainLoader).Assembly.Location;

        // Act
        var plugin = loader.LoadPlugin(assemblyPath, "NonExistentType", tempDir);

        // Assert
        plugin.Should().BeNull();
    }

    [Fact]
    public void LoadPlugin_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var loader = new PluginDomainLoader();
        var tempDir = CreateTempDirectory();
        loader.Dispose();

        // Act & Assert
        var action = () => loader.LoadPlugin("test.dll", "TestType", tempDir);
        action.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var loader = new PluginDomainLoader();

        // Act & Assert - Should not throw
        loader.Dispose();
        loader.Dispose();
        loader.Dispose();
    }

    [Fact]
    public void Constructor_ShouldNotThrow()
    {
        // Arrange & Act
        var action = () => new PluginDomainLoader();

        // Assert - Should not throw even if AppDomains aren't supported
        action.Should().NotThrow();
    }

    [Fact]
    public void PluginDomainProxy_InitializeLifetimeService_ShouldReturnNull()
    {
        // Arrange
        var proxy = new PluginDomainProxy();

        // Act
        var result = proxy.InitializeLifetimeService();

        // Assert
        result.Should().BeNull();

        // Cleanup
        proxy.Dispose();
    }

    [Fact]
    public void PluginDomainProxy_LoadPlugin_WithNonExistentAssembly_ShouldReturnNull()
    {
        // Arrange
        using var proxy = new PluginDomainProxy();
        var tempDir = CreateTempDirectory();

        // Act
        var plugin = proxy.LoadPlugin("nonexistent.dll", "TestType", tempDir);

        // Assert
        plugin.Should().BeNull();
    }

    [Fact]
    public void PluginDomainProxy_LoadPlugin_AfterDispose_ShouldReturnNull()
    {
        // Arrange
        var proxy = new PluginDomainProxy();
        var tempDir = CreateTempDirectory();
        proxy.Dispose();

        // Act
        var plugin = proxy.LoadPlugin("test.dll", "TestType", tempDir);

        // Assert
        plugin.Should().BeNull();
    }

    [Fact]
    public void PluginDomainProxy_Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var proxy = new PluginDomainProxy();

        // Act & Assert - Should not throw
        proxy.Dispose();
        proxy.Dispose();
        proxy.Dispose();
    }

    private string CreateTempDirectory()
    {
        var tempDir = TestHelpers.CreateTempDirectory();
        _tempDirectories.Add(tempDir);
        return tempDir;
    }

    public void Dispose()
    {
        foreach (var tempDir in _tempDirectories)
        {
            TestHelpers.CleanupTempDirectory(tempDir);
        }
        _tempDirectories.Clear();
    }
}