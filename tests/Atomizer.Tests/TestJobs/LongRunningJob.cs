namespace Atomizer.Tests.TestJobs;

public record LongRunningJobPayload(int DurationInSeconds);

public class LongRunningJob : IAtomizerJob<LongRunningJobPayload>
{
    public async Task HandleAsync(LongRunningJobPayload payload, JobContext context)
    {
        await Task.Delay(payload.DurationInSeconds * 1000, context.CancellationToken);
    }
}
