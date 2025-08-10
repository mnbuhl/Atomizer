using Atomizer.Abstractions;

namespace Atomizer.Example.Handlers;

public record LoggerJob(string Message, LogLevel Level);

public class LoggerHandler : IJobHandler<LoggerJob>
{
    private readonly ILogger<LoggerHandler> _logger;

    public LoggerHandler(ILogger<LoggerHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(LoggerJob payload, JobContext context)
    {
        _logger.Log(payload.Level, "{Message}", payload.Message);
        return Task.CompletedTask;
    }
}
