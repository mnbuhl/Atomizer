namespace Atomizer.Redis.Example.Handlers;

public record AccountEvent(string AccountId, string EventName);

public class AccountEventJob : IAtomizerJob<AccountEvent>
{
    private readonly ILogger<AccountEventJob> _logger;

    public AccountEventJob(ILogger<AccountEventJob> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(AccountEvent payload, JobContext context)
    {
        _logger.LogInformation(
            "Handled account event {EventName} for account {AccountId} in job {JobId}",
            payload.EventName,
            payload.AccountId,
            context.Job.Id
        );
        return Task.CompletedTask;
    }
}
