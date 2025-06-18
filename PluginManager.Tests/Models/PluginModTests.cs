using FluentAssertions;
using PluginManager.Core.Models;
using Xunit;

namespace PluginManager.Tests.Models;

public class PluginModTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithDefaults()
    {
        // Act
        var mod = new PluginMod();

        // Assert
        mod.Name.Should().BeEmpty();
        mod.ModUrl.Should().BeEmpty();
        mod.DownloadUrl.Should().BeEmpty();
        mod.ImageUrl.Should().BeEmpty();
        mod.Publisher.Should().BeEmpty();
        mod.Type.Should().BeEmpty();
        mod.UploadDate.Should().Be(default);
        mod.FileSize.Should().Be(0);
        mod.PluginSource.Should().BeEmpty();
    }

    [Fact]
    public void Properties_ShouldBeSettable()
    {
        // Arrange
        var mod = new PluginMod();
        var uploadDate = DateTime.UtcNow;

        // Act
        mod.Name = "Test Mod";
        mod.ModUrl = "https://example.com/mod";
        mod.DownloadUrl = "https://example.com/download";
        mod.ImageUrl = "https://example.com/image.jpg";
        mod.Publisher = "Test Publisher";
        mod.Type = "Equipment";
        mod.UploadDate = uploadDate;
        mod.FileSize = 1024;
        mod.PluginSource = "test-plugin";

        // Assert
        mod.Name.Should().Be("Test Mod");
        mod.ModUrl.Should().Be("https://example.com/mod");
        mod.DownloadUrl.Should().Be("https://example.com/download");
        mod.ImageUrl.Should().Be("https://example.com/image.jpg");
        mod.Publisher.Should().Be("Test Publisher");
        mod.Type.Should().Be("Equipment");
        mod.UploadDate.Should().Be(uploadDate);
        mod.FileSize.Should().Be(1024);
        mod.PluginSource.Should().Be("test-plugin");
    }
}