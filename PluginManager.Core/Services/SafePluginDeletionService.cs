using Microsoft.Extensions.Logging;
using PluginManager.Core.Interfaces;

namespace PluginManager.Core.Services;

public class SafePluginDeletionService : ISafePluginDeletionService
{
    private readonly ILogger<SafePluginDeletionService> _logger;
    private readonly EnhancedPluginService _pluginService;

    public SafePluginDeletionService(
        ILogger<SafePluginDeletionService> logger,
        EnhancedPluginService pluginService)
    {
        _logger = logger;
        _pluginService = pluginService;
    }

    public async Task<bool> SafeDeletePluginDirectoryAsync(string pluginId, string pluginDirectory,
        TimeSpan? timeout = null)
    {
        var actualTimeout = timeout ?? TimeSpan.FromMinutes(2);
        var startTime = DateTime.UtcNow;

        _logger.LogInformation("Starting safe deletion of plugin {PluginId} from {Directory}",
            pluginId, pluginDirectory);

        await _pluginService.UnregisterPluginAsync(pluginId);

        while (DateTime.UtcNow - startTime < actualTimeout)
        {
            var canDelete = await _pluginService.CanPluginBeDeletedAsync(pluginId);
            if (canDelete)
            {
                break;
            }

            _logger.LogDebug("Waiting for plugin {PluginId} to be fully unloaded...", pluginId);
            await Task.Delay(500);
        }

        var finalCheck = await _pluginService.CanPluginBeDeletedAsync(pluginId);
        if (!finalCheck)
        {
            _logger.LogWarning("Plugin {PluginId} may not be fully unloaded, attempting deletion anyway", pluginId);
        }

        return await RetryDeleteDirectoryAsync(pluginDirectory, maxRetries: 5);
    }

    public async Task<bool> CanDirectoryBeDeletedAsync(string pluginDirectory)
    {
        if (!Directory.Exists(pluginDirectory))
            return true;

        try
        {
            var files = Directory.GetFiles(pluginDirectory, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                try
                {
                    using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None);
                    // File is accessible
                }
                catch (IOException)
                {
                    _logger.LogDebug("File is locked: {File}", file);
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking directory accessibility: {Directory}", pluginDirectory);
            return false;
        }
    }

    private async Task<bool> RetryDeleteDirectoryAsync(string directory, int maxRetries = 5)
    {
        if (!Directory.Exists(directory))
        {
            _logger.LogDebug("Directory {Directory} does not exist", directory);
            return true;
        }

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Force garbage collection before each attempt
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                // Try to delete
                Directory.Delete(directory, true);

                _logger.LogInformation("Successfully deleted directory {Directory} on attempt {Attempt}",
                    directory, attempt);
                return true;
            }
            catch (UnauthorizedAccessException ex) when (attempt < maxRetries)
            {
                _logger.LogDebug("Attempt {Attempt} failed to delete {Directory}: {Error}",
                    attempt, directory, ex.Message);

                // Check which files are still locked
                await LogLockedFilesAsync(directory);

                // Wait with exponential backoff
                var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
                await Task.Delay(delay);
            }
            catch (DirectoryNotFoundException)
            {
                _logger.LogDebug("Directory {Directory} was already deleted", directory);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error deleting directory {Directory} on attempt {Attempt}",
                    directory, attempt);

                if (attempt == maxRetries)
                    throw;
            }
        }

        _logger.LogError("Failed to delete directory {Directory} after {MaxRetries} attempts",
            directory, maxRetries);
        return false;
    }

    private async Task LogLockedFilesAsync(string directory)
    {
        try
        {
            var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            foreach (var file in files.Take(10)) // Limit to avoid spam
            {
                try
                {
                    using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None);
                    // File is not locked
                }
                catch (IOException)
                {
                    _logger.LogDebug("File appears to be locked: {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking for locked files in {Directory}", directory);
        }
    }
}