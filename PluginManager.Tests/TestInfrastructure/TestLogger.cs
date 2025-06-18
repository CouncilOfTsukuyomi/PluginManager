using Microsoft.Extensions.Logging;

namespace PluginManager.Tests.TestInfrastructure;

/// <summary>
/// Test logger that captures log entries for verification
/// </summary>
public class TestLogger<T> : ILogger<T>
{
    private readonly TestLogger _logger;

    public TestLogger()
    {
        _logger = new TestLogger(typeof(T).Name);
    }

    public IReadOnlyList<LogEntry> LogEntries => _logger.LogEntries;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return _logger.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return _logger.IsEnabled(logLevel);
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _logger.Log(logLevel, eventId, state, exception, formatter);
    }
}

/// <summary>
/// Non-generic test logger implementation
/// </summary>
public class TestLogger : ILogger
{
    private readonly List<LogEntry> _logEntries = new();
    private readonly string _categoryName;

    public TestLogger(string categoryName)
    {
        _categoryName = categoryName;
    }

    public IReadOnlyList<LogEntry> LogEntries => _logEntries.AsReadOnly();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return new TestScope(state?.ToString() ?? "");
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true; // Enable all log levels for testing
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var logEntry = new LogEntry
        {
            LogLevel = logLevel,
            EventId = eventId,
            Category = _categoryName,
            Message = message,
            Exception = exception,
            State = state,
            Timestamp = DateTime.UtcNow
        };

        _logEntries.Add(logEntry);
    }

    public void Clear()
    {
        _logEntries.Clear();
    }
}

/// <summary>
/// Represents a single log entry for testing
/// </summary>
public class LogEntry
{
    public LogLevel LogLevel { get; set; }
    public EventId EventId { get; set; }
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public Exception? Exception { get; set; }
    public object? State { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Test scope implementation
/// </summary>
public class TestScope : IDisposable
{
    public string ScopeValue { get; }

    public TestScope(string scopeValue)
    {
        ScopeValue = scopeValue;
    }

    public void Dispose()
    {
        // Nothing to dispose for test scope
    }
}

/// <summary>
/// Test logger provider for dependency injection
/// </summary>
public class TestLoggerProvider : ILoggerProvider
{
    private readonly Dictionary<string, TestLogger> _loggers = new();

    public ILogger CreateLogger(string categoryName)
    {
        if (!_loggers.ContainsKey(categoryName))
        {
            _loggers[categoryName] = new TestLogger(categoryName);
        }
        return _loggers[categoryName];
    }

    public TestLogger<T> GetLogger<T>()
    {
        var categoryName = typeof(T).Name;
        if (!_loggers.ContainsKey(categoryName))
        {
            _loggers[categoryName] = new TestLogger(categoryName);
        }
        return new TestLogger<T>();
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}