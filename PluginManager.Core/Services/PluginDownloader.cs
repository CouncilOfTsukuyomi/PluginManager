using System.IO.Compression;
using Microsoft.Extensions.Logging;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;

namespace PluginManager.Core.Services;

public class PluginDownloader : IPluginDownloader
{
    private readonly ILogger<PluginDownloader> _logger;
    private readonly ISafePluginDeletionService? _deletionService;

    public PluginDownloader(
        ILogger<PluginDownloader> logger,
        ISafePluginDeletionService? deletionService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _deletionService = deletionService;
    }

    public async Task<PluginDownloadResult> DownloadAsync(string downloadUrl, string? fileName = null)
    {
        try
        {
            _logger.LogInformation("Downloading plugin from {Url}", downloadUrl);
            
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5); // Longer timeout for downloads
            
            var response = await httpClient.GetAsync(downloadUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                return new PluginDownloadResult
                {
                    Success = false,
                    ErrorMessage = $"Download failed: {response.StatusCode} {response.ReasonPhrase}"
                };
            }

            var pluginData = await response.Content.ReadAsByteArrayAsync();
            var extractedFileName = fileName ?? ExtractFileNameFromUrl(downloadUrl) ?? "plugin.zip";

            _logger.LogInformation("Successfully downloaded plugin, size: {Size} bytes", pluginData.Length);

            return new PluginDownloadResult
            {
                Success = true,
                PluginData = pluginData,
                FileName = extractedFileName,
                FileSize = pluginData.Length,
                ContentType = response.Content.Headers.ContentType?.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading plugin from {Url}", downloadUrl);
            return new PluginDownloadResult
            {
                Success = false,
                ErrorMessage = $"Download failed: {ex.Message}"
            };
        }
    }

    public async Task<PluginDownloadResult> DownloadAsync(DefaultPluginInfo pluginInfo)
    {
        return await DownloadAsync(pluginInfo.DownloadUrl, $"{pluginInfo.Id}.zip");
    }

    public async Task<PluginInstallResult> DownloadAndInstallAsync(DefaultPluginInfo pluginInfo, string? pluginsBasePath = null)
    {
        try
        {
            _logger.LogInformation("Downloading and installing plugin: {PluginName} ({PluginId}) v{Version}", 
                pluginInfo.Name, pluginInfo.Id, pluginInfo.Version);
            
            var downloadResult = await DownloadAsync(pluginInfo);
            
            if (!downloadResult.Success)
            {
                return new PluginInstallResult
                {
                    Success = false,
                    ErrorMessage = $"Download failed: {downloadResult.ErrorMessage}",
                    PluginId = pluginInfo.Id,
                    PluginName = pluginInfo.Name,
                    Version = pluginInfo.Version
                };
            }
            
            var pluginsPath = pluginsBasePath ?? Path.Combine(AppContext.BaseDirectory, "plugins");
            Directory.CreateDirectory(pluginsPath);
            
            var pluginDir = Path.Combine(pluginsPath, pluginInfo.Id);
            
            if (Directory.Exists(pluginDir))
            {
                _logger.LogInformation("Removing existing plugin installation at {PluginDir}", pluginDir);
                
                if (_deletionService != null)
                {
                    var deletionSuccess = await _deletionService.SafeDeletePluginDirectoryAsync(
                        pluginInfo.Id, pluginDir, TimeSpan.FromMinutes(1));
                    
                    if (!deletionSuccess)
                    {
                        _logger.LogWarning("Failed to safely delete existing plugin directory, attempting force delete");
                        try
                        {
                            Directory.Delete(pluginDir, true);
                        }
                        catch (Exception ex)
                        {
                            return new PluginInstallResult
                            {
                                Success = false,
                                ErrorMessage = $"Could not remove existing plugin installation: {ex.Message}",
                                PluginId = pluginInfo.Id,
                                PluginName = pluginInfo.Name,
                                Version = pluginInfo.Version
                            };
                        }
                    }
                }
                else
                {
                    Directory.Delete(pluginDir, true);
                }
            }
            
            Directory.CreateDirectory(pluginDir);

            // Handle installation based on file type
            var fileExtension = Path.GetExtension(downloadResult.FileName).ToLowerInvariant();
            
            if (fileExtension == ".zip")
            {
                // Extract ZIP file
                await ExtractZipFileAsync(downloadResult.PluginData, pluginDir);
                _logger.LogInformation("Extracted ZIP plugin to {PluginDir}", pluginDir);
            }
            else
            {
                // Save as single file
                var targetPath = Path.Combine(pluginDir, downloadResult.FileName);
                await File.WriteAllBytesAsync(targetPath, downloadResult.PluginData);
                _logger.LogInformation("Saved plugin file to {TargetPath}", targetPath);
            }

            return new PluginInstallResult
            {
                Success = true,
                InstalledPath = pluginDir,
                FileSize = downloadResult.FileSize ?? 0,
                PluginId = pluginInfo.Id,
                PluginName = pluginInfo.Name,
                Version = pluginInfo.Version
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install plugin: {PluginName} ({PluginId})", 
                pluginInfo.Name, pluginInfo.Id);
                
            return new PluginInstallResult
            {
                Success = false,
                ErrorMessage = $"Installation failed: {ex.Message}",
                PluginId = pluginInfo.Id,
                PluginName = pluginInfo.Name,
                Version = pluginInfo.Version
            };
        }
    }

    /// <summary>
    /// Safely uninstalls a plugin by removing its directory
    /// </summary>
    public async Task<bool> UninstallPluginAsync(string pluginId, string pluginDirectory)
    {
        if (_deletionService == null)
        {
            _logger.LogWarning("No deletion service available, performing direct deletion for plugin {PluginId}", pluginId);
            try
            {
                if (Directory.Exists(pluginDirectory))
                {
                    Directory.Delete(pluginDirectory, true);
                    _logger.LogInformation("Deleted plugin directory: {PluginDirectory}", pluginDirectory);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete plugin directory: {PluginDirectory}", pluginDirectory);
                return false;
            }
        }

        return await _deletionService.SafeDeletePluginDirectoryAsync(pluginId, pluginDirectory);
    }

    private static async Task ExtractZipFileAsync(byte[] zipData, string extractPath)
    {
        using var zipStream = new MemoryStream(zipData);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        
        foreach (var entry in archive.Entries)
        {
            // Skip directory entries
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var entryPath = Path.Combine(extractPath, entry.FullName);
            var entryDir = Path.GetDirectoryName(entryPath);
            
            if (!string.IsNullOrEmpty(entryDir))
            {
                Directory.CreateDirectory(entryDir);
            }

            using var entryStream = entry.Open();
            using var fileStream = File.Create(entryPath);
            await entryStream.CopyToAsync(fileStream);
        }
    }

    private static string ExtractFileNameFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var fileName = Path.GetFileName(uri.LocalPath);
            return string.IsNullOrEmpty(fileName) ? "plugin.zip" : fileName;
        }
        catch
        {
            return "plugin.zip";
        }
    }
}