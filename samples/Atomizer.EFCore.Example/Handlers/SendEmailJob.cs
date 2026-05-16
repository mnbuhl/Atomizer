namespace Atomizer.EFCore.Example.Handlers;

public record SendEmailPayload;

public class SendEmailJob : IAtomizerJob<SendEmailPayload>
{
    public Task HandleAsync(SendEmailPayload payload, JobContext context)
    {
        // Simulate sending an email by waiting for a short time.
        return Task.Delay(TimeSpan.FromSeconds(5), context.CancellationToken);
    }
}
