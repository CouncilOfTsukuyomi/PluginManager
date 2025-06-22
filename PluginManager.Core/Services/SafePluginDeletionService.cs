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
        var actualTimeout = timeout ?? TimeSpan.FromMinutes(3);
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

            _logger.LogDebug("Waiting for plugin {PluginId} AssemblyLoadContext to be collected...", pluginId);
            await Task.Delay(1000); 
        }

        var finalCheck = await _pluginService.CanPluginBeDeletedAsync(pluginId);
        if (!finalCheck)
        {
            _logger.LogWarning("Plugin {PluginId} AssemblyLoadContext may not be fully collected after {Timeout}s, attempting deletion anyway", 
                pluginId, actualTimeout.TotalSeconds);
        }

        return await RetryDeleteDirectoryAsync(pluginDirectory, maxRetries: 10);
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

    private async Task<bool> RetryDeleteDirectoryAsync(string directory, int maxRetries = 10)
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
                // Aggressive garbage collection for AssemblyLoadContext cleanup
                for (int i = 0; i < 5; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    await Task.Delay(300);
                }
                
                Directory.Delete(directory, true);

                _logger.LogInformation("Successfully deleted directory {Directory} on attempt {Attempt}",
                    directory, attempt);
                return true;
            }
            catch (UnauthorizedAccessException ex) when (attempt < maxRetries)
            {
                _logger.LogDebug("Attempt {Attempt} failed to delete {Directory}: {Error}",
                    attempt, directory, ex.Message);
                
                await LogLockedFilesAsync(directory);
                
                var delay = TimeSpan.FromMilliseconds(Math.Min(500 * Math.Pow(1.8, attempt - 1), 8000));
                _logger.LogDebug("Waiting {DelayMs}ms before retry {NextAttempt}", delay.TotalMilliseconds, attempt + 1);
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
                {
                    _logger.LogError("Failed to delete directory {Directory} after {MaxRetries} attempts. " +
                                   "Plugin files may still be locked by the runtime.", directory, maxRetries);
                    return false;
                }
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
            var lockedFiles = new List<string>();
            
            foreach (var file in files.Take(20))
            {
                try
                {
                    using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None);
                }
                catch (IOException)
                {
                    lockedFiles.Add(Path.GetFileName(file));
                }
            }
            
            if (lockedFiles.Any())
            {
                _logger.LogDebug("Locked files detected: {LockedFiles}", string.Join(", ", lockedFiles));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking for locked files in {Directory}", directory);
        }
    }
}