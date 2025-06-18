using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PluginManager.Core.Extensions;
using PluginManager.Core.Interfaces;
using PluginManager.Tests.TestInfrastructure;
using Xunit;

namespace PluginManager.Tests.Integration;

public class PluginSystemIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _tempDir;
    private readonly string _pluginDir;

    public PluginSystemIntegrationTests()
    {
        _tempDir = TestHelpers.CreateTempDirectory();
        _pluginDir = Path.Combine(_tempDir, "plugins");
        Directory.CreateDirectory(_pluginDir);
        
        var services = new ServiceCollection();
        var loggerProvider = TestHelpers.CreateLoggerProvider();
        services.AddLogging(builder => builder.AddProvider(loggerProvider));
        
        // Add plugin services with explicit path
        services.AddPluginServices(_pluginDir);
        
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        TestHelpers.CleanupTempDirectory(_tempDir);
    }

    [Fact]
    public async Task PluginService_FullWorkflow_ShouldWork()
    {
        // Arrange
        var pluginService = _serviceProvider.GetRequiredService<IPluginService>();

        // Act & Assert - Service should be injectable
        pluginService.Should().NotBeNull();
        
        // Should start with no plugins
        var initialPlugins = pluginService.GetAllPlugins();
        initialPlugins.Should().BeEmpty();
        
        // Register a mock plugin
        var mockPlugin = new MockPlugin();
        await pluginService.RegisterPluginAsync(mockPlugin);
        
        // Should now have one plugin
        var plugins = pluginService.GetAllPlugins();
        plugins.Should().HaveCount(1);
        
        // Should be able to get the plugin by ID
        var retrievedPlugin = pluginService.GetPlugin(mockPlugin.PluginId);
        retrievedPlugin.Should().NotBeNull();
        retrievedPlugin!.PluginId.Should().Be(mockPlugin.PluginId);
        
        // Should be able to get mods
        mockPlugin.ModsToReturn.Add(TestHelpers.CreateTestMod());
        var mods = await pluginService.GetAllRecentModsAsync();
        mods.Should().HaveCount(1);
        
        // Should be able to unregister
        await pluginService.UnregisterPluginAsync(mockPlugin.PluginId);
        var finalPlugins = pluginService.GetAllPlugins();
        finalPlugins.Should().BeEmpty();
    }

    [Fact]
    public async Task PluginService_WithMaliciousPlugin_ShouldNotCrash()
    {
        // Arrange
        var pluginService = _serviceProvider.GetRequiredService<IPluginService>();
        var maliciousPlugin = new MaliciousPlugin();

        // Act
        await pluginService.RegisterPluginAsync(maliciousPlugin);
        
        // Try to get mods (should not crash)
        var mods = await pluginService.GetAllRecentModsAsync();

        // Assert - System should remain stable
        mods.Should().NotBeNull("Should not crash when getting mods");
        
        // Basic safety checks
        foreach (var mod in mods)
        {
            mod.Should().NotBeNull("Each mod should not be null");
            mod.Name.Should().NotBeNull("Mod name should not be null");
            mod.Publisher.Length.Should().BeLessOrEqualTo(2000, "Publisher length should be limited");
        }
    }

    [Fact]
    public void ServiceRegistration_ShouldRegisterAllRequiredServices()
    {
        // Act & Assert
        var pluginService = _serviceProvider.GetService<IPluginService>();
        var discoveryService = _serviceProvider.GetService<IPluginDiscoveryService>();
        var logger = _serviceProvider.GetService<ILogger<IPluginService>>();

        pluginService.Should().NotBeNull();
        discoveryService.Should().NotBeNull();
        logger.Should().NotBeNull();
    }

    [Fact]
    public async Task EnhancedPluginService_InitializeAsync_ShouldNotThrow()
    {
        // Arrange - Cast to the concrete type that has InitializeAsync
        var concreteService = _serviceProvider.GetRequiredService<IPluginService>() as PluginManager.Core.Services.EnhancedPluginService;
        concreteService.Should().NotBeNull("Service should be EnhancedPluginService");

        // Act & Assert
        Func<Task> act = async () => await concreteService!.InitializeAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PluginDiscoveryService_GetAllPluginInfoAsync_ShouldReturnEmptyList()
    {
        // Arrange
        var discoveryService = _serviceProvider.GetRequiredService<IPluginDiscoveryService>();

        // Act
        var plugins = await discoveryService.GetAllPluginInfoAsync();

        // Assert
        plugins.Should().NotBeNull();
        plugins.Should().BeEmpty(); // No plugins in test directory
    }

    [Fact]
    public async Task PluginService_GetAllRecentModsAsync_WithNoPlugins_ShouldReturnEmptyList()
    {
        // Arrange
        var pluginService = _serviceProvider.GetRequiredService<IPluginService>();

        // Act
        var mods = await pluginService.GetAllRecentModsAsync();

        // Assert
        mods.Should().NotBeNull();
        mods.Should().BeEmpty();
    }

    [Fact]
    public async Task PluginService_GetRecentModsFromPluginAsync_WithNonExistentPlugin_ShouldReturnEmptyList()
    {
        // Arrange
        var pluginService = _serviceProvider.GetRequiredService<IPluginService>();

        // Act
        var mods = await pluginService.GetRecentModsFromPluginAsync("non-existent-plugin");

        // Assert
        mods.Should().NotBeNull();
        mods.Should().BeEmpty();
    }
}