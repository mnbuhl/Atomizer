namespace Atomizer.Example.Handlers;

public record LoggerJob(string Message, LogLevel Level);

public class Logger : IAtomizerJob<LoggerJob>
{
    private readonly ILogger<Logger> _logger;

    public Logger(ILogger<Logger> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(LoggerJob payload, JobContext context)
    {
        _logger.Log(payload.Level, "{Message}", payload.Message);
        return Task.CompletedTask;
    }
}
