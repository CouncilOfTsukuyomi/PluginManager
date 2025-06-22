namespace PluginManager.Core.Interfaces;

public interface ISafePluginDeletionService
{
    /// <summary>
    /// Safely deletes a plugin directory after ensuring the plugin is properly unloaded
    /// </summary>
    /// <param name="pluginId">The ID of the plugin to delete</param>
    /// <param name="pluginDirectory">The directory path to delete</param>
    /// <param name="timeout">Maximum time to wait for unloading (default: 2 minutes)</param>
    /// <returns>True if deletion was successful, false otherwise</returns>
    Task<bool> SafeDeletePluginDirectoryAsync(string pluginId, string pluginDirectory, TimeSpan? timeout = null);
    
    /// <summary>
    /// Checks if a plugin directory can be safely deleted (no locked files)
    /// </summary>
    /// <param name="pluginDirectory">The directory to check</param>
    /// <returns>True if directory can be deleted, false if files are locked</returns>
    Task<bool> CanDirectoryBeDeletedAsync(string pluginDirectory);
}