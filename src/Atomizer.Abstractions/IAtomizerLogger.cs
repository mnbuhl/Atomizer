using System;

namespace Atomizer.Abstractions
{
    public interface IAtomizerLogger
    {
        void Log(AtomizerLogLevel level, string message, params object?[] args);
        void Log(AtomizerLogLevel level, Exception ex, string? message, params object?[] args);
        void LogDebug(string message, params object?[] args);
        void LogInformation(string message, params object?[] args);
        void LogWarning(string message, params object?[] args);
        void LogWarning(Exception ex, string? message, params object?[] args);
        void LogError(string message, params object?[] args);
        void LogError(Exception ex, string? message, params object?[] args);
    }

    public enum AtomizerLogLevel
    {
        Debug,
        Information,
        Warning,
        Error,
        None,
    }
}
