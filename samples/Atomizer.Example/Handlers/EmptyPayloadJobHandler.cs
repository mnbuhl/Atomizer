using Atomizer.Abstractions;

namespace Atomizer.Example.Handlers;

public record EmptyPayloadJob;

public class EmptyPayloadJobHandler : IAtomizerJobHandler<EmptyPayloadJob>
{
    private readonly ILogger<EmptyPayloadJobHandler> _logger;

    public EmptyPayloadJobHandler(ILogger<EmptyPayloadJobHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(EmptyPayloadJob payload, JobContext context)
    {
        _logger.LogInformation("Handling EmptyPayloadJob with ID: {JobId}", context.Job.Id);
        // Simulate some processing
        return Task.CompletedTask;
    }
}
