namespace PluginManager.Core.Models;

public class DefaultPluginRegistryOptions
{
    public required string RegistryUrl { get; set; }
    public bool UseGitHubIntegration { get; set; } = false;
}