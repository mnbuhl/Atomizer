namespace Atomizer.Example.Handlers;

public record ExceptionJobPayload(string Message);

public class ExceptionJob : IAtomizerJob<ExceptionJobPayload>
{
    public Task HandleAsync(ExceptionJobPayload payload, JobContext context)
    {
        // Simulate a job that always fails
        throw new InvalidOperationException($"This job always fails with message: {payload.Message}");
    }
}
