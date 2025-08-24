using Microsoft.Extensions.Logging;

namespace Atomizer.Tests.Utilities;

public abstract class TestableLogger : ILogger
{
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        var message = formatter(state, exception);
        switch (logLevel)
        {
            case LogLevel.Information:
                LogInformation(message);
                break;
            case LogLevel.Error when exception == null:
                LogError(message);
                break;
            case LogLevel.Warning when exception == null:
                LogWarning(message);
                break;
            case LogLevel.Debug:
                LogDebug(message);
                break;
            case LogLevel.Trace:
                LogTrace(message);
                break;
            case LogLevel.Critical when exception == null:
                LogCritical(message);
                break;
            case LogLevel.Critical:
                LogCritical(exception, message);
                break;
            case LogLevel.Error:
                LogError(exception, message);
                break;
            case LogLevel.Warning:
                LogWarning(exception, message);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null);
        }
    }

    public abstract void LogInformation(string message);
    public abstract void LogError(string message);
    public abstract void LogWarning(string message);
    public abstract void LogDebug(string message);
    public abstract void LogTrace(string message);
    public abstract void LogCritical(string message);
    public abstract void LogCritical(Exception exception, string message);
    public abstract void LogError(Exception exception, string message);
    public abstract void LogWarning(Exception exception, string message);

    public abstract bool IsEnabled(LogLevel logLevel);

    public abstract IDisposable BeginScope<TState>(TState state);
}

public abstract class TestableLogger<T> : TestableLogger, ILogger<T>;
