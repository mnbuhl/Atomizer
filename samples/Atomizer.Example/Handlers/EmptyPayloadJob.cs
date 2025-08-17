namespace Atomizer.Example.Handlers;

public record EmptyPayload;

public class EmptyPayloadJob : IAtomizerJob<EmptyPayload>
{
    private readonly ILogger<EmptyPayloadJob> _logger;

    public EmptyPayloadJob(ILogger<EmptyPayloadJob> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(EmptyPayload payload, JobContext context)
    {
        _logger.LogInformation("Handling EmptyPayloadJob with ID: {JobId}", context.Job.Id);
        // Simulate some processing
        return Task.CompletedTask;
    }
}
