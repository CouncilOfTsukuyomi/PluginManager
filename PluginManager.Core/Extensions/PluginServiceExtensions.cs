using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;
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
        services.AddSingleton<IPluginManagementService>(provider =>
            provider.GetRequiredService<EnhancedPluginService>());

        return services;
    }

    /// <summary>
    /// Add default plugin services (for downloading plugins from remote registry)
    /// </summary>
    public static IServiceCollection AddDefaultPluginServices(
        this IServiceCollection services,
        string registryUrl,
        bool useGitHubIntegration = false)
    {
        if (string.IsNullOrWhiteSpace(registryUrl))
            throw new ArgumentException("Registry URL cannot be null or empty", nameof(registryUrl));

        // Register plugin downloader
        services.AddScoped<IPluginDownloader, PluginDownloader>();

        if (useGitHubIntegration)
        {
            // Register GitHub plugin provider with HttpClient
            services.AddHttpClient<IGitHubPluginProvider, GitHubPluginProvider>();

            // Register enhanced registry service with GitHub integration
            services.AddScoped<IDefaultPluginRegistryService>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<EnhancedDefaultPluginRegistryService>>();
                var gitHubProvider = provider.GetRequiredService<IGitHubPluginProvider>();
                return new EnhancedDefaultPluginRegistryService(registryUrl, logger, gitHubProvider);
            });
        }
        else
        {
            // Register basic registry service
            services.AddScoped<IDefaultPluginRegistryService>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<DefaultPluginRegistryService>>();
                return new DefaultPluginRegistryService(registryUrl, logger);
            });
        }

        return services;
    }

    /// <summary>
    /// Add default plugin services with configuration options
    /// </summary>
    public static IServiceCollection AddDefaultPluginServices(
        this IServiceCollection services,
        Action<DefaultPluginRegistryOptions> configureOptions)
    {
        var options = new DefaultPluginRegistryOptions {RegistryUrl = string.Empty};
        configureOptions(options);

        if (string.IsNullOrWhiteSpace(options.RegistryUrl))
            throw new ArgumentException("RegistryUrl must be configured");

        return services.AddDefaultPluginServices(options.RegistryUrl, options.UseGitHubIntegration);
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
