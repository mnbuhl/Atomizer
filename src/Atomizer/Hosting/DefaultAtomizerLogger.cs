using System;
using Atomizer.Abstractions;
using Microsoft.Extensions.Logging;

// ReSharper disable TemplateIsNotCompileTimeConstantProblem

namespace Atomizer.Hosting
{
    public class DefaultAtomizerLogger : IAtomizerLogger
    {
        private readonly ILogger _logger;

        public DefaultAtomizerLogger(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger("Atomizer");
        }

        public void Log(AtomizerLogLevel level, string message, params object?[] args)
        {
            _logger.Log(ConvertLogLevel(level), message, args);
        }

        public void Log(AtomizerLogLevel level, Exception ex, string? message, params object?[] args)
        {
            _logger.Log(ConvertLogLevel(level), ex, message, args);
        }

        public void LogDebug(string message, params object?[] args) => Log(AtomizerLogLevel.Debug, message, args);

        public void LogInformation(string message, params object?[] args) =>
            Log(AtomizerLogLevel.Information, message, args);

        public void LogWarning(string message, params object?[] args) => Log(AtomizerLogLevel.Warning, message, args);

        public void LogWarning(Exception ex, string? message, params object?[] args) =>
            Log(AtomizerLogLevel.Warning, ex, message, args);

        public void LogError(string message, params object?[] args) => Log(AtomizerLogLevel.Error, message, args);

        public void LogError(Exception ex, string? message, params object?[] args) =>
            Log(AtomizerLogLevel.Error, ex, message, args);

        private static LogLevel ConvertLogLevel(AtomizerLogLevel level)
        {
            return level switch
            {
                AtomizerLogLevel.Debug => LogLevel.Debug,
                AtomizerLogLevel.Information => LogLevel.Information,
                AtomizerLogLevel.Warning => LogLevel.Warning,
                AtomizerLogLevel.Error => LogLevel.Error,
                _ => LogLevel.None,
            };
        }
    }
}
