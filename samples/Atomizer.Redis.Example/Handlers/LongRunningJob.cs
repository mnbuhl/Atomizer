namespace Atomizer.Redis.Example.Handlers;

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
            "Handling Redis long-running job {JobId} for {Duration} seconds",
            context.Job.Id,
            payload.DurationInSeconds
        );

        await Task.Delay(TimeSpan.FromSeconds(payload.DurationInSeconds), context.CancellationToken);

        _logger.LogInformation(
            "Completed Redis long-running job {JobId} after {Duration} seconds",
            context.Job.Id,
            payload.DurationInSeconds
        );
    }
}
