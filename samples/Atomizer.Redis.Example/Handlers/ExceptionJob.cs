namespace Atomizer.Redis.Example.Handlers;

public record ExceptionJobPayload(string Message);

public class ExceptionJob : IAtomizerJob<ExceptionJobPayload>
{
    public Task HandleAsync(ExceptionJobPayload payload, JobContext context)
    {
        throw new InvalidOperationException($"This job always fails with message: {payload.Message}");
    }
}
