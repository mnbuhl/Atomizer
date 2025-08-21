namespace Atomizer.EFCore.Example.Handlers;

public record LoggerJobPayload(string Message, LogLevel Level);

public class LoggerJob : IAtomizerJob<LoggerJobPayload>
{
    private readonly ILogger<LoggerJob> _logger;

    public LoggerJob(ILogger<LoggerJob> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(LoggerJobPayload payload, JobContext context)
    {
        _logger.Log(payload.Level, "{Message}", payload.Message);
        return Task.CompletedTask;
    }
}
