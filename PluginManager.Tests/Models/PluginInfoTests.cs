using FluentAssertions;
using PluginManager.Core.Models;
using Xunit;

namespace PluginManager.Tests.Models;

public class PluginInfoTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithDefaults()
    {
        // Act
        var pluginInfo = new PluginInfo();

        // Assert
        pluginInfo.PluginId.Should().BeEmpty();
        pluginInfo.DisplayName.Should().BeEmpty();
        pluginInfo.Description.Should().BeEmpty();
        pluginInfo.Version.Should().BeEmpty();
        pluginInfo.AssemblyPath.Should().BeEmpty();
        pluginInfo.TypeName.Should().BeEmpty();
        pluginInfo.PluginDirectory.Should().BeEmpty();
        pluginInfo.IsEnabled.Should().BeFalse();
        pluginInfo.Configuration.Should().NotBeNull().And.BeEmpty();
        pluginInfo.Author.Should().BeEmpty();
        pluginInfo.LastModified.Should().Be(default);
        pluginInfo.IsLoaded.Should().BeFalse();
        pluginInfo.LoadError.Should().BeNull();
    }

    [Fact]
    public void Properties_ShouldBeSettable()
    {
        // Arrange
        var pluginInfo = new PluginInfo();
        var config = new Dictionary<string, object> { { "key", "value" } };
        var lastModified = DateTime.UtcNow;

        // Act
        pluginInfo.PluginId = "test-plugin";
        pluginInfo.DisplayName = "Test Plugin";
        pluginInfo.Description = "A test plugin";
        pluginInfo.Version = "1.0.0";
        pluginInfo.AssemblyPath = "/path/to/plugin.dll";
        pluginInfo.TypeName = "TestPlugin.MainClass";
        pluginInfo.PluginDirectory = "/path/to/plugin";
        pluginInfo.IsEnabled = false;
        pluginInfo.Configuration = config;
        pluginInfo.Author = "Test Author";
        pluginInfo.LastModified = lastModified;
        pluginInfo.IsLoaded = true;
        pluginInfo.LoadError = "Test error";

        // Assert
        pluginInfo.PluginId.Should().Be("test-plugin");
        pluginInfo.DisplayName.Should().Be("Test Plugin");
        pluginInfo.Description.Should().Be("A test plugin");
        pluginInfo.Version.Should().Be("1.0.0");
        pluginInfo.AssemblyPath.Should().Be("/path/to/plugin.dll");
        pluginInfo.TypeName.Should().Be("TestPlugin.MainClass");
        pluginInfo.PluginDirectory.Should().Be("/path/to/plugin");
        pluginInfo.IsEnabled.Should().BeFalse();
        pluginInfo.Configuration.Should().BeEquivalentTo(config);
        pluginInfo.Author.Should().Be("Test Author");
        pluginInfo.LastModified.Should().Be(lastModified);
        pluginInfo.IsLoaded.Should().BeTrue();
        pluginInfo.LoadError.Should().Be("Test error");
    }
}