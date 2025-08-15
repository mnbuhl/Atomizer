namespace Atomizer.EFCore.Example.Handlers;

public record ExceptionJob(string Message);

public class ExceptionJobHandler : IAtomizerJobHandler<ExceptionJob>
{
    public Task HandleAsync(ExceptionJob payload, JobContext context)
    {
        // Simulate a job that always fails
        throw new InvalidOperationException($"This job always fails with message: {payload.Message}");
    }
}
