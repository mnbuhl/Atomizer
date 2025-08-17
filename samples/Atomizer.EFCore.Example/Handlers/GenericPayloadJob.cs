namespace Atomizer.EFCore.Example.Handlers;

public record GenericPayload<TPayload>(TPayload Payload);

public class GenericPayloadJob : IAtomizerJob<GenericPayload<string>>
{
    private readonly ILogger<GenericPayloadJob> _logger;

    public GenericPayloadJob(ILogger<GenericPayloadJob> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(GenericPayload<string> payload, JobContext context)
    {
        _logger.LogInformation("Handling job with payload: {@Payload}", payload.Payload);
        return Task.CompletedTask;
    }
}
