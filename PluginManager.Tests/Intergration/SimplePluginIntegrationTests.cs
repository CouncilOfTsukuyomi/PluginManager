using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;
using PluginManager.Core.Services;
using PluginManager.Tests.TestInfrastructure;
using Xunit;

namespace PluginManager.Tests.Integration;

public class SimplePluginIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<IPluginDiscoveryService> _mockDiscoveryService;
    private readonly string _tempDir;

    public SimplePluginIntegrationTests()
    {
        _tempDir = TestHelpers.CreateTempDirectory();
        _mockDiscoveryService = new Mock<IPluginDiscoveryService>();
        
        var services = new ServiceCollection();
        var loggerProvider = TestHelpers.CreateLoggerProvider();
        services.AddLogging(builder => builder.AddProvider(loggerProvider));
        
        // Register mocked discovery service
        services.AddSingleton(_mockDiscoveryService.Object);
        // Use EnhancedPluginService which has security features
        services.AddSingleton<IPluginService, EnhancedPluginService>();
        
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        TestHelpers.CleanupTempDirectory(_tempDir);
    }

    [Fact]
    public async Task PluginService_WithMockedDiscovery_ShouldWork()
    {
        // Arrange
        var pluginService = _serviceProvider.GetRequiredService<IPluginService>();
        var pluginInfo = TestHelpers.CreateTestPluginInfo();
        
        _mockDiscoveryService.Setup(x => x.GetAllPluginInfoAsync())
            .ReturnsAsync(new List<PluginInfo> { pluginInfo });

        // Act & Assert
        pluginService.Should().NotBeNull();
        
        // Test basic plugin registration
        var mockPlugin = new MockPlugin();
        await pluginService.RegisterPluginAsync(mockPlugin);
        
        var plugins = pluginService.GetAllPlugins();
        plugins.Should().HaveCount(1);
        
        // Test getting mods
        mockPlugin.ModsToReturn.Add(TestHelpers.CreateTestMod());
        var mods = await pluginService.GetAllRecentModsAsync();
        mods.Should().HaveCount(1);
        mods[0].PluginSource.Should().Be(mockPlugin.PluginId);
    }

    [Fact]
    public async Task PluginService_SecurityEnforcement_ShouldWork()
    {
        // Arrange
        var pluginService = _serviceProvider.GetRequiredService<IPluginService>();
        var maliciousPlugin = new MaliciousPlugin();

        // Act
        await pluginService.RegisterPluginAsync(maliciousPlugin);
        var mods = await pluginService.GetAllRecentModsAsync();

        // Assert - Security should be enforced
        mods.Should().NotBeEmpty();
        foreach (var mod in mods)
        {
            // HTML tags should be removed (SecurityPluginProxy removes HTML tags)
            mod.Name.Should().NotContain("<script>", "HTML script tags should be removed");
            mod.Name.Should().NotContain("</script>", "HTML script tags should be removed");
            mod.Publisher.Should().NotContain("<script>", "HTML script tags should be removed");
            
            // Dangerous URLs should be blocked
            mod.ModUrl.Should().NotStartWith("file://", "File URLs should be blocked");
            mod.DownloadUrl.Should().NotStartWith("javascript:", "JavaScript URLs should be blocked");
            mod.ImageUrl.Should().NotContain("localhost", "Localhost URLs should be blocked");
            
            // String length should be limited to MaxStringLength (2000)
            mod.Publisher.Length.Should().BeLessOrEqualTo(2000, "Publisher length should be limited");
            mod.Name.Length.Should().BeLessOrEqualTo(2000, "Name length should be limited");
            mod.Version.Length.Should().BeLessOrEqualTo(2000, "Version length should be limited");
            
            // Verify content exists (security proxy should still return content, even if sanitized)
            mod.Name.Should().NotBeNullOrEmpty("Name should not be empty");
            mod.Publisher.Should().NotBeNullOrEmpty("Publisher should not be empty");
        }
        
        // Should be limited to reasonable number of mods (MaxModsPerCall = 200)
        mods.Should().HaveCountLessOrEqualTo(200, "Should be limited to MaxModsPerCall");
    }

    [Fact]
    public async Task PluginService_HtmlTagSanitization_ShouldWork()
    {
        // Arrange
        var pluginService = _serviceProvider.GetRequiredService<IPluginService>();
        
        // Create a plugin that returns mods with HTML tags
        var htmlPlugin = new MockPlugin { PluginId = "html-test" };
        htmlPlugin.ModsToReturn.Add(new PluginMod
        {
            Name = "<script>alert('xss')</script>Test Mod<div>content</div>",
            Publisher = "<b>Publisher</b><script>malicious()</script>",
            ModUrl = "https://example.com/mod",
            DownloadUrl = "https://example.com/download",
            ImageUrl = "https://example.com/image.jpg",
            Type = "Mod",
            Version = "1.0.0",
            UploadDate = DateTime.UtcNow.AddDays(-1),
            FileSize = 1024000,
            PluginSource = htmlPlugin.PluginId
        });

        // Act
        await pluginService.RegisterPluginAsync(htmlPlugin);
        var mods = await pluginService.GetAllRecentModsAsync();

        // Assert - HTML tags should be removed but text content should remain
        mods.Should().HaveCount(1);
        var mod = mods[0];
        
        // HTML tags should be stripped, but text content should remain
        mod.Name.Should().NotContain("<script>");
        mod.Name.Should().NotContain("</script>");
        mod.Name.Should().NotContain("<div>");
        mod.Name.Should().NotContain("</div>");
        mod.Name.Should().Contain("Test Mod"); // Text content should remain
        
        mod.Publisher.Should().NotContain("<b>");
        mod.Publisher.Should().NotContain("</b>");
        mod.Publisher.Should().NotContain("<script>");
        mod.Publisher.Should().Contain("Publisher"); // Text content should remain
        
        // Version should be properly set and sanitized
        mod.Version.Should().Be("1.0.0");
    }

    [Fact]
    public void ServiceRegistration_ShouldWork()
    {
        // Act & Assert
        var pluginService = _serviceProvider.GetService<IPluginService>();
        var discoveryService = _serviceProvider.GetService<IPluginDiscoveryService>();

        pluginService.Should().NotBeNull();
        discoveryService.Should().NotBeNull();
    }

    [Fact] 
    public async Task PluginService_RegisterAndUnregister_ShouldWork()
    {
        // Arrange
        var pluginService = _serviceProvider.GetRequiredService<IPluginService>();
        var mockPlugin = new MockPlugin { PluginId = "test-plugin-123" };

        // Act - Register
        await pluginService.RegisterPluginAsync(mockPlugin);
        
        // Assert - Plugin should be registered
        var registeredPlugin = pluginService.GetPlugin("test-plugin-123");
        registeredPlugin.Should().NotBeNull();
        registeredPlugin!.PluginId.Should().Be("test-plugin-123");
        
        // Act - Unregister
        await pluginService.UnregisterPluginAsync("test-plugin-123");
        
        // Assert - Plugin should be removed
        var unregisteredPlugin = pluginService.GetPlugin("test-plugin-123");
        unregisteredPlugin.Should().BeNull();
    }

    [Fact]
    public async Task PluginService_GetEnabledPlugins_ShouldOnlyReturnEnabled()
    {
        // Arrange
        var pluginService = _serviceProvider.GetRequiredService<IPluginService>();
        var enabledPlugin = new MockPlugin { PluginId = "enabled", IsEnabled = true };
        var disabledPlugin = new MockPlugin { PluginId = "disabled", IsEnabled = false };

        // Act
        await pluginService.RegisterPluginAsync(enabledPlugin);
        await pluginService.RegisterPluginAsync(disabledPlugin);
        
        var enabledPlugins = pluginService.GetEnabledPlugins();

        // Assert
        enabledPlugins.Should().HaveCount(1);
        enabledPlugins[0].PluginId.Should().Be("enabled");
    }
}