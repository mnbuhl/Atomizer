using Atomizer.EFCore.Example.Data;
using Atomizer.EFCore.Example.Data.Postgres;

namespace Atomizer.EFCore.Example.Handlers;

public record CleanupProductsBefore(DateTime BeforeDate);

public class CleanupProductsJob : IAtomizerJob<CleanupProductsBefore>
{
    private readonly ExamplePostgresContext _dbContext;
    private readonly ILogger<CleanupProductsJob> _logger;

    public CleanupProductsJob(ExamplePostgresContext dbContext, ILogger<CleanupProductsJob> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public Task HandleAsync(CleanupProductsBefore payload, JobContext context)
    {
        return Task.CompletedTask;
    }
}
