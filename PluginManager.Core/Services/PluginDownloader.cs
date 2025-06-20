using Microsoft.Extensions.Logging;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;

namespace PluginManager.Core.Services;

public class PluginDownloader : IPluginDownloader
{
    private readonly ILogger<PluginDownloader> _logger;

    public PluginDownloader(ILogger<PluginDownloader> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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