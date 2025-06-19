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
        
        // Register plugin services with explicit configuration
        services.AddSingleton<IPluginDiscoveryService>(provider =>
            new PluginDiscoveryService(
                provider.GetRequiredService<ILogger<PluginDiscoveryService>>(),
                basePath));
        
        services.AddSingleton<EnhancedPluginService>();
        services.AddSingleton<IPluginService>(provider => provider.GetRequiredService<EnhancedPluginService>());
        services.AddSingleton<IPluginManagementService>(provider => provider.GetRequiredService<EnhancedPluginService>());
        
        return services;
    }
}