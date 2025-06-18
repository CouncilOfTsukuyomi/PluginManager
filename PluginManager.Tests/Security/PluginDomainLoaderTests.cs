using FluentAssertions;
using PluginManager.Core.Security;
using PluginManager.Tests.TestInfrastructure;
using Xunit;

namespace PluginManager.Tests.Security;

public class PluginDomainLoaderTests
{
    [Fact]
    public async Task LoadPluginAsync_WithNonExistentAssembly_ShouldReturnNull()
    {
        // Arrange
        var loader = new PluginDomainLoader();
        var tempDir = TestHelpers.CreateTempDirectory();

        try
        {
            // Act
            var plugin = await loader.LoadPluginAsync("nonexistent.dll", "TestType", tempDir);

            // Assert
            plugin.Should().BeNull();
        }
        finally
        {
            TestHelpers.CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public async Task LoadPluginAsync_WithInvalidTypeName_ShouldReturnNull()
    {
        // Arrange
        var loader = new PluginDomainLoader();
        var tempDir = TestHelpers.CreateTempDirectory();

        try
        {
            // Create a dummy assembly file
            var assemblyPath = Path.Combine(tempDir, "test.dll");
            await File.WriteAllBytesAsync(assemblyPath, new byte[] { 0x4D, 0x5A }); // Invalid PE header

            // Act
            var plugin = await loader.LoadPluginAsync(assemblyPath, "NonExistentType", tempDir);

            // Assert
            plugin.Should().BeNull();
        }
        finally
        {
            TestHelpers.CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public void InitializeLifetimeService_ShouldReturnNull()
    {
        // Arrange
        var loader = new PluginDomainLoader();

        // Act
        var result = loader.InitializeLifetimeService();

        // Assert
        result.Should().BeNull();
    }
}