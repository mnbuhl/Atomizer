namespace Atomizer.EFCore.Example.Handlers;

public record LongRunningJobPayload(int DurationInSeconds);

public class LongRunningJob : IAtomizerJob<LongRunningJobPayload>
{
    private readonly ILogger<LongRunningJob> _logger;

    public LongRunningJob(ILogger<LongRunningJob> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(LongRunningJobPayload payload, JobContext context)
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
