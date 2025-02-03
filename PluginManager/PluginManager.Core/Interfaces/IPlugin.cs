using NLog;

namespace PluginManager.Core.Interfaces;

public interface IPlugin
{
    string PluginName { get; }
    void Execute();
    protected ILogger Logger { get; }
}