using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Services;

namespace PluginManager.Core.Extensions;

public static class PluginServiceExtensions
{
    /// <summary>
    /// Add plugin management services to the DI container
    /// </summary>
    public static IServiceCollection AddPluginServices(this IServiceCollection services, string? pluginBasePath = null)
    {
        // Use default path if none provided
        var basePath = pluginBasePath ?? Path.Combine(AppContext.BaseDirectory, "plugins");
        
        // Register PluginRegistryService first (dependency for PluginDiscoveryService)
        services.AddSingleton<PluginRegistryService>(provider =>
            new PluginRegistryService(
                provider.GetRequiredService<ILogger<PluginRegistryService>>(),
                basePath));
        
        // Register plugin discovery service with all required dependencies
        services.AddSingleton<IPluginDiscoveryService>(provider =>
            new PluginDiscoveryService(
                provider.GetRequiredService<ILogger<PluginDiscoveryService>>(),
                basePath,
                provider.GetRequiredService<PluginRegistryService>()));
        
        // Register enhanced plugin service
        services.AddSingleton<EnhancedPluginService>();
        services.AddSingleton<IPluginService>(provider => provider.GetRequiredService<EnhancedPluginService>());
        services.AddSingleton<IPluginManagementService>(provider => provider.GetRequiredService<EnhancedPluginService>());
        
        return services;
    }
    
    /// <summary>
    /// Initialise plugin services (call this after DI container is built)
    /// </summary>
    public static async Task InitializePluginServicesAsync(this IServiceProvider serviceProvider)
    {
        // Initialise the registry service
        var registryService = serviceProvider.GetRequiredService<PluginRegistryService>();
        await registryService.InitializeAsync();
        
        // Initialise the enhanced plugin service
        var pluginService = serviceProvider.GetRequiredService<EnhancedPluginService>();
        await pluginService.InitializeAsync();
    }
}