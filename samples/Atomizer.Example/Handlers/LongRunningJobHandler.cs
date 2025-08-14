namespace Atomizer.Example.Handlers;

public record LongRunningJob(int DurationInSeconds);

public class LongRunningJobHandler : IAtomizerJobHandler<LongRunningJob>
{
    private readonly ILogger<LongRunningJobHandler> _logger;

    public LongRunningJobHandler(ILogger<LongRunningJobHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(LongRunningJob payload, JobContext context)
    {
        _logger.LogInformation(
            "Handling LongRunningJob with ID: {JobId}, Duration: {Duration} seconds",
            context.Job.Id,
            payload.DurationInSeconds
        );

        // Simulate a long-running job
        await Task.Delay(TimeSpan.FromSeconds(payload.DurationInSeconds), context.CancellationToken);

        _logger.LogInformation(
            "Completed LongRunningJob with ID: {JobId} after {Duration} seconds",
            context.Job.Id,
            payload.DurationInSeconds
        );
    }
}
