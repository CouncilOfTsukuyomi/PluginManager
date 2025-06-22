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

        // Register safe plugin deletion service
        services.AddTransient<ISafePluginDeletionService, SafePluginDeletionService>();

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

        // Register plugin downloader (depends on safe deletion service)
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

            // Register plugin update service when GitHub integration is enabled
            services.AddScoped<IPluginUpdateService, PluginUpdateService>();
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
    /// Add plugin update services specifically (can be called independently)
    /// </summary>
    public static IServiceCollection AddPluginUpdateServices(this IServiceCollection services)
    {
        // Register GitHub plugin provider with HttpClient if not already registered
        services.AddHttpClient<IGitHubPluginProvider, GitHubPluginProvider>();
        
        // Register plugin update service
        services.AddScoped<IPluginUpdateService, PluginUpdateService>();
        
        return services;
    }

    /// <summary>
    /// Add plugin update services with custom HttpClient configuration
    /// </summary>
    public static IServiceCollection AddPluginUpdateServices(
        this IServiceCollection services, 
        Action<HttpClient> configureHttpClient)
    {
        // Register GitHub plugin provider with custom HttpClient configuration
        services.AddHttpClient<IGitHubPluginProvider, GitHubPluginProvider>(configureHttpClient);
        
        // Register plugin update service
        services.AddScoped<IPluginUpdateService, PluginUpdateService>();
        
        return services;
    }

    /// <summary>
    /// Add comprehensive plugin services with update capabilities and safe deletion
    /// </summary>
    public static IServiceCollection AddPluginServicesWithUpdates(
        this IServiceCollection services,
        string registryUrl,
        string? pluginBasePath = null,
        Action<HttpClient>? configureHttpClient = null)
    {
        if (string.IsNullOrWhiteSpace(registryUrl))
            throw new ArgumentException("Registry URL cannot be null or empty", nameof(registryUrl));

        // Add core plugin services (includes safe deletion service)
        services.AddPluginServices(pluginBasePath);
        
        // Add default plugin services with GitHub integration enabled
        services.AddDefaultPluginServices(registryUrl, useGitHubIntegration: true);
        
        // Add update services with optional HttpClient configuration
        if (configureHttpClient != null)
        {
            services.AddPluginUpdateServices(configureHttpClient);
        }
        else
        {
            services.AddPluginUpdateServices();
        }
        
        return services;
    }

    /// <summary>
    /// Add comprehensive plugin services with update capabilities using configuration options
    /// </summary>
    public static IServiceCollection AddPluginServicesWithUpdates(
        this IServiceCollection services,
        Action<PluginServicesOptions> configureOptions)
    {
        var options = new PluginServicesOptions();
        configureOptions(options);

        if (string.IsNullOrWhiteSpace(options.RegistryUrl))
            throw new ArgumentException("RegistryUrl must be configured");

        // Add core plugin services (includes safe deletion service)
        services.AddPluginServices(options.PluginBasePath);
        
        // Add default plugin services with GitHub integration
        services.AddDefaultPluginServices(options.RegistryUrl, useGitHubIntegration: true);
        
        // Add update services with optional HttpClient configuration
        if (options.ConfigureHttpClient != null)
        {
            services.AddPluginUpdateServices(options.ConfigureHttpClient);
        }
        else
        {
            services.AddPluginUpdateServices();
        }
        
        return services;
    }

    /// <summary>
    /// Add only the safe plugin deletion service (useful for testing or custom scenarios)
    /// </summary>
    public static IServiceCollection AddSafePluginDeletion(this IServiceCollection services)
    {
        services.AddTransient<ISafePluginDeletionService, SafePluginDeletionService>();
        return services;
    }

    /// <summary>
    /// Add plugin services with custom safe deletion service implementation
    /// </summary>
    public static IServiceCollection AddPluginServices<TSafeDeletionService>(
        this IServiceCollection services, 
        string? pluginBasePath = null)
        where TSafeDeletionService : class, ISafePluginDeletionService
    {
        // Use default path if none provided
        var basePath = pluginBasePath ?? Path.Combine(AppContext.BaseDirectory, "plugins");

        // Register PluginRegistryService first
        services.AddSingleton<PluginRegistryService>(provider =>
            new PluginRegistryService(
                provider.GetRequiredService<ILogger<PluginRegistryService>>(),
                basePath));

        // Register plugin discovery service
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

        // Register custom safe plugin deletion service
        services.AddTransient<ISafePluginDeletionService, TSafeDeletionService>();

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