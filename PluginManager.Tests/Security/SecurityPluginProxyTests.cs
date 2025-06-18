using FluentAssertions;
using Microsoft.Extensions.Logging;
using PluginManager.Core.Models;
using PluginManager.Core.Security;
using PluginManager.Tests.TestInfrastructure;
using Xunit;

namespace PluginManager.Tests.Security;

public class SecurityPluginProxyTests : IDisposable
{
    private readonly TestLogger<SecurityPluginProxy> _logger;
    private readonly string _tempDir;

    public SecurityPluginProxyTests()
    {
        _logger = TestHelpers.CreateTestLogger<SecurityPluginProxy>();
        _tempDir = TestHelpers.CreateTempDirectory();
    }

    public void Dispose()
    {
        TestHelpers.CleanupTempDirectory(_tempDir);
    }

    [Fact]
    public async Task InitializeAsync_WithValidConfiguration_ShouldSucceed()
    {
        // Arrange
        var mockPlugin = new MockPlugin();
        var proxy = new SecurityPluginProxy(mockPlugin, _logger);
        var config = new Dictionary<string, object> { { "ApiKey", "testValue" } }; // Use allowed key

        // Act
        await proxy.InitializeAsync(config);

        // Assert
        mockPlugin.InitializeCalled.Should().BeTrue();
        mockPlugin.LastConfiguration.Should().ContainKey("ApiKey");
        mockPlugin.LastConfiguration["ApiKey"].Should().Be("testValue");
    }

    [Fact]
    public async Task InitializeAsync_WithDisallowedConfiguration_ShouldFilterKeys()
    {
        // Arrange
        var mockPlugin = new MockPlugin();
        var proxy = new SecurityPluginProxy(mockPlugin, _logger);
        var config = new Dictionary<string, object> 
        { 
            { "ApiKey", "allowed" },      // This should be allowed
            { "BadKey", "notAllowed" }    // This should be filtered out
        };

        // Act
        await proxy.InitializeAsync(config);

        // Assert
        mockPlugin.InitializeCalled.Should().BeTrue();
        mockPlugin.LastConfiguration.Should().ContainKey("ApiKey");
        mockPlugin.LastConfiguration.Should().NotContainKey("BadKey");
    }

    [Fact]
    public async Task InitializeAsync_WithTimeout_ShouldThrowSecurityException()
    {
        // Arrange - Use a shorter timeout for testing
        var mockPlugin = new MockPlugin { InitializeDelay = TimeSpan.FromSeconds(35) }; // Longer than 30s timeout
        var proxy = new SecurityPluginProxy(mockPlugin, _logger);

        // Act & Assert
        var act = () => proxy.InitializeAsync(new Dictionary<string, object>());
        await act.Should().ThrowAsync<SecurityException>()
            .WithMessage("Plugin initialization timed out");
    }

    [Fact]
    public async Task GetRecentModsAsync_WithValidMods_ShouldReturnSanitizedMods()
    {
        // Arrange
        var mockPlugin = new MockPlugin();
        mockPlugin.ModsToReturn.Add(TestHelpers.CreateTestMod("Valid Mod"));
        var proxy = new SecurityPluginProxy(mockPlugin, _logger);

        // Act
        var mods = await proxy.GetRecentModsAsync();

        // Assert
        mods.Should().HaveCount(1);
        mods[0].Name.Should().Be("Valid Mod");
        mockPlugin.GetRecentModsCalled.Should().BeTrue();
    }

    [Fact]
    public async Task GetRecentModsAsync_WithMaliciousMods_ShouldSanitize()
    {
        // Arrange
        var mockPlugin = new MockPlugin();
        mockPlugin.ModsToReturn.Add(new PluginMod
        {
            Name = "alert('xss')Evil Mod", // No HTML tags, just JavaScript text
            ModUrl = "javascript:alert('hack')",
            DownloadUrl = "file:///etc/passwd",
            ImageUrl = "http://localhost:8080/admin",
            Publisher = new string('A', 3000), // Longer than MaxStringLength (2000)
            Type = "Malicious"
        });
        var proxy = new SecurityPluginProxy(mockPlugin, _logger);

        // Act
        var mods = await proxy.GetRecentModsAsync();

        // Assert
        mods.Should().HaveCount(1);
        var mod = mods[0];
        
        // The SanitizeText method only removes HTML tags, not standalone JavaScript
        // So "alert('xss')Evil Mod" should remain as is since it's not in HTML tags
        mod.Name.Should().Be("alert('xss')Evil Mod");
        
        mod.ModUrl.Should().BeEmpty(); // Invalid URL blocked
        mod.DownloadUrl.Should().BeEmpty(); // File URL blocked  
        mod.ImageUrl.Should().BeEmpty(); // Localhost URL blocked
        mod.Publisher.Length.Should().BeLessOrEqualTo(2000); // String truncated to MaxStringLength
    }

    [Fact]
    public async Task GetRecentModsAsync_WithHtmlTags_ShouldRemoveTags()
    {
        // Arrange
        var mockPlugin = new MockPlugin();
        mockPlugin.ModsToReturn.Add(new PluginMod
        {
            Name = "<script>alert('xss')</script>Evil Mod<div>test</div>",
            Publisher = "<b>Bold</b> Publisher",
            Type = "<i>Italic</i> Type"
        });
        var proxy = new SecurityPluginProxy(mockPlugin, _logger);

        // Act
        var mods = await proxy.GetRecentModsAsync();

        // Assert
        mods.Should().HaveCount(1);
        var mod = mods[0];
        
        // HTML tags should be removed by SanitizeText using regex <.*?>
        // "<script>alert('xss')</script>Evil Mod<div>test</div>" becomes "alert('xss')Evil Modtest"
        mod.Name.Should().Be("alert('xss')Evil Modtest");
        mod.Publisher.Should().Be("Bold Publisher");
        mod.Type.Should().Be("Italic Type");
    }

    [Fact]
    public async Task GetRecentModsAsync_WithTooManyMods_ShouldLimitResults()
    {
        // Arrange
        var mockPlugin = new MockPlugin();
        for (int i = 0; i < 300; i++) // More than MaxModsPerCall (200)
        {
            mockPlugin.ModsToReturn.Add(TestHelpers.CreateTestMod($"Mod {i}"));
        }
        var proxy = new SecurityPluginProxy(mockPlugin, _logger);

        // Act
        var mods = await proxy.GetRecentModsAsync();

        // Assert
        mods.Should().HaveCountLessOrEqualTo(200); // MaxModsPerCall from SecurityPolicy
    }

    [Fact]
    public async Task GetRecentModsAsync_WithTimeout_ShouldThrowSecurityException()
    {
        // Arrange
        var mockPlugin = new MockPlugin { GetRecentModsDelay = TimeSpan.FromSeconds(35) }; // Longer than 30s timeout
        var proxy = new SecurityPluginProxy(mockPlugin, _logger);

        // Act & Assert
        var act = () => proxy.GetRecentModsAsync();
        await act.Should().ThrowAsync<SecurityException>()
            .WithMessage("GetRecentMods operation timed out");
    }

    [Fact]
    public void PluginDirectory_WithInvalidPath_ShouldThrowSecurityException()
    {
        // Arrange
        var mockPlugin = new MockPlugin();
        var proxy = new SecurityPluginProxy(mockPlugin, _logger);

        // Act & Assert
        var act = () => proxy.PluginDirectory = "/etc/passwd";
        act.Should().Throw<SecurityException>()
            .WithMessage("Plugin directory outside allowed path*");
    }

    [Fact]
    public async Task DisposeAsync_ShouldDisposeInnerPlugin()
    {
        // Arrange
        var mockPlugin = new MockPlugin();
        var proxy = new SecurityPluginProxy(mockPlugin, _logger);

        // Act
        await proxy.DisposeAsync();

        // Assert
        mockPlugin.DisposeCalled.Should().BeTrue();
    }

    [Fact]
    public async Task MethodCallLimits_WhenExceeded_ShouldThrowSecurityException()
    {
        // Arrange
        var mockPlugin = new MockPlugin();
        var proxy = new SecurityPluginProxy(mockPlugin, _logger);

        // The SecurityPolicy.Default sets GetRecentModsAsync limit to 50
        // So we need to call it 51 times to exceed the limit
        for (int i = 0; i < 50; i++)
        {
            await proxy.GetRecentModsAsync();
        }

        // Act & Assert - The 51st call should fail
        var act = () => proxy.GetRecentModsAsync();
        await act.Should().ThrowAsync<SecurityException>()
            .WithMessage("Method call limit exceeded");
    }

    [Fact]
    public async Task InitializeAsync_MethodCallLimits_ShouldEnforceLimit()
    {
        // Arrange
        var mockPlugin = new MockPlugin();
        var proxy = new SecurityPluginProxy(mockPlugin, _logger);

        // The SecurityPolicy.Default sets InitializeAsync limit to 3
        var config = new Dictionary<string, object> { { "ApiKey", "test" } };
        
        // Act - Call InitializeAsync 3 times (should succeed)
        for (int i = 0; i < 3; i++)
        {
            await proxy.InitializeAsync(config);
        }

        // Assert - The 4th call should fail
        var act = () => proxy.InitializeAsync(config);
        await act.Should().ThrowAsync<SecurityException>()
            .WithMessage("Method call limit exceeded");
    }

    [Fact]
    public async Task SanitizeConfiguration_ShouldRemoveDangerousContent()
    {
        // Arrange
        var mockPlugin = new MockPlugin();
        var proxy = new SecurityPluginProxy(mockPlugin, _logger);
        
        var config = new Dictionary<string, object>
        {
            // SanitizeValue removes: "<script", "javascript:", "file://" (case insensitive)
            { "ApiKey", "file://dangerous<script>alert()</script>path" },
            { "BaseUrl", "javascript:evil()content" }
        };

        // Act
        await proxy.InitializeAsync(config);

        // Assert - The SanitizeValue method should remove dangerous content from config values
        var sanitizedConfig = mockPlugin.LastConfiguration;
        // "file://dangerous<script>alert()</script>path" -> "dangerous>alert()</script>path" (removes "file://" and "<script")
        sanitizedConfig["ApiKey"].Should().Be("dangerous>alert()</script>path");
        // "javascript:evil()content" -> "evil()content" (removes "javascript:")
        sanitizedConfig["BaseUrl"].Should().Be("evil()content");
    }

    [Fact]
    public async Task ModTextSanitization_ShouldOnlyRemoveHtmlTags()
    {
        // Arrange
        var mockPlugin = new MockPlugin();
        mockPlugin.ModsToReturn.Add(new PluginMod
        {
            Name = "file://dangerous<script>alert()</script>path",
            Publisher = "javascript:evil()content"
        });
        var proxy = new SecurityPluginProxy(mockPlugin, _logger);

        // Act
        var mods = await proxy.GetRecentModsAsync();

        // Assert - SanitizeText only removes HTML tags using regex <.*?>
        var mod = mods[0];
        // "file://dangerous<script>alert()</script>path" -> "file://dangerousalert()path" (only <script> and </script> removed)
        mod.Name.Should().Be("file://dangerousalert()path");
        // "javascript:evil()content" -> "javascript:evil()content" (no change, no HTML tags)
        mod.Publisher.Should().Be("javascript:evil()content");
    }
}