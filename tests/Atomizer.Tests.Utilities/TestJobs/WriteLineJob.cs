namespace Atomizer.Tests.Utilities.TestJobs;

public record WriteLineMessage(string Message);

public class WriteLineJob : IAtomizerJob<WriteLineMessage>
{
    public Task HandleAsync(WriteLineMessage payload, JobContext context)
    {
        Console.WriteLine(payload.Message);
        return Task.CompletedTask;
    }
}
