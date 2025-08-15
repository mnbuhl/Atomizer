namespace Atomizer.EFCore.Example.Handlers;

public record GenericPayload<TPayload>(TPayload Payload);

public class GenericPayloadJobHandler : IAtomizerJobHandler<GenericPayload<string>>
{
    private readonly ILogger<GenericPayloadJobHandler> _logger;

    public GenericPayloadJobHandler(ILogger<GenericPayloadJobHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(GenericPayload<string> payload, JobContext context)
    {
        _logger.LogInformation("Handling job with payload: {@Payload}", payload.Payload);
        return Task.CompletedTask;
    }
}
