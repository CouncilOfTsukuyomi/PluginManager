
using Microsoft.Extensions.Logging;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;

namespace PluginManager.Core.Security;

/// <summary>
/// Proxy that wraps an isolated plugin instance and implements IModPlugin in the main context
/// This solves the type identity issue where interfaces from different assembly contexts are incompatible
/// </summary>
internal class IsolatedPluginProxy : IModPlugin
{
    private readonly object _pluginInstance;
    private readonly Type _pluginType;
    private readonly ILogger _logger;

    public IsolatedPluginProxy(object pluginInstance, string pluginDirectory, ILogger logger)
    {
        _pluginInstance = pluginInstance ?? throw new ArgumentNullException(nameof(pluginInstance));
        _pluginType = pluginInstance.GetType();
        _logger = logger;
        PluginDirectory = pluginDirectory;
    }

    public string PluginId => GetStringProperty("PluginId");
    public string DisplayName => GetStringProperty("DisplayName");
    public string Description => GetStringProperty("Description");
    public string Version => GetStringProperty("Version");
    public string Author => GetStringProperty("Author");
    
    public bool IsEnabled 
    { 
        get => GetBoolProperty("IsEnabled");
        set => SetBoolProperty("IsEnabled", value);
    }
    
    public string PluginDirectory 
    { 
        get => GetStringProperty("PluginDirectory");
        set => SetStringProperty("PluginDirectory", value);
    }

    public async Task<List<PluginMod>> GetRecentModsAsync()
    {
        try
        {
            var method = _pluginType.GetMethod("GetRecentModsAsync");
            if (method != null)
            {
                var result = method.Invoke(_pluginInstance, null);
                if (result is Task task)
                {
                    await task;
                    
                    // Get the result from the completed task
                    var resultProperty = task.GetType().GetProperty("Result");
                    if (resultProperty?.GetValue(task) is IEnumerable<object> pluginMods)
                    {
                        return ConvertToPluginMods(pluginMods);
                    }
                }
            }
            
            return new List<PluginMod>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling GetRecentModsAsync on isolated plugin");
            return new List<PluginMod>();
        }
    }

    public async Task InitializeAsync(Dictionary<string, object> configuration)
    {
        try
        {
            var method = _pluginType.GetMethod("InitializeAsync");
            if (method != null)
            {
                var result = method.Invoke(_pluginInstance, new object[] { configuration });
                if (result is Task task)
                {
                    await task;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling InitializeAsync on isolated plugin");
            throw;
        }
    }

    public void RequestCancellation()
    {
        try
        {
            var method = _pluginType.GetMethod("RequestCancellation");
            if (method != null)
            {
                method.Invoke(_pluginInstance, null);
                _logger.LogDebug("RequestCancellation called on isolated plugin {PluginId}", PluginId);
            }
            else
            {
                _logger.LogWarning("RequestCancellation method not found on isolated plugin {PluginId}", PluginId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling RequestCancellation on isolated plugin {PluginId}", PluginId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            var method = _pluginType.GetMethod("DisposeAsync");
            if (method != null)
            {
                var result = method.Invoke(_pluginInstance, null);
                if (result is ValueTask valueTask)
                {
                    await valueTask;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling DisposeAsync on isolated plugin");
        }
    }

    private string GetStringProperty(string propertyName)
    {
        try
        {
            var property = _pluginType.GetProperty(propertyName);
            return property?.GetValue(_pluginInstance)?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private bool GetBoolProperty(string propertyName)
    {
        try
        {
            var property = _pluginType.GetProperty(propertyName);
            return (bool)(property?.GetValue(_pluginInstance) ?? false);
        }
        catch
        {
            return false;
        }
    }

    private void SetBoolProperty(string propertyName, bool value)
    {
        try
        {
            var property = _pluginType.GetProperty(propertyName);
            property?.SetValue(_pluginInstance, value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set property {PropertyName}", propertyName);
        }
    }

    private void SetStringProperty(string propertyName, string value)
    {
        try
        {
            var property = _pluginType.GetProperty(propertyName);
            property?.SetValue(_pluginInstance, value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set property {PropertyName}", propertyName);
        }
    }

    private List<PluginMod> ConvertToPluginMods(IEnumerable<object> sourcePluginMods)
    {
        var result = new List<PluginMod>();
        
        foreach (var sourceMod in sourcePluginMods)
        {
            try
            {
                var sourceType = sourceMod.GetType();
                var pluginMod = new PluginMod();

                // Copy properties by name using reflection
                CopyProperty(sourceMod, pluginMod, "Name");
                CopyProperty(sourceMod, pluginMod, "Author");
                CopyProperty(sourceMod, pluginMod, "Description");
                CopyProperty(sourceMod, pluginMod, "ModUrl");
                CopyProperty(sourceMod, pluginMod, "DownloadUrl");
                CopyProperty(sourceMod, pluginMod, "ImageUrl");
                CopyProperty(sourceMod, pluginMod, "Version");
                CopyProperty(sourceMod, pluginMod, "Tags");
                CopyProperty(sourceMod, pluginMod, "PluginSource");
                
                result.Add(pluginMod);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to convert plugin mod object");
            }
        }

        return result;
    }

    private void CopyProperty(object source, object target, string propertyName)
    {
        try
        {
            var sourceProp = source.GetType().GetProperty(propertyName);
            var targetProp = target.GetType().GetProperty(propertyName);
            
            if (sourceProp != null && targetProp != null && targetProp.CanWrite)
            {
                var value = sourceProp.GetValue(source);
                targetProp.SetValue(target, value);
            }
        }
        catch
        {
            // Ignore property copy failures
        }
    }
}