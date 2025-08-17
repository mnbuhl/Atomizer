using Atomizer.EFCore.Example.Data;

namespace Atomizer.EFCore.Example.Handlers;

public record CleanupProductsBefore(DateTime BeforeDate);

public class CleanupProductsJob : IAtomizerJob<CleanupProductsBefore>
{
    private readonly ExampleDbContext _dbContext;
    private readonly ILogger<CleanupProductsJob> _logger;

    public CleanupProductsJob(ExampleDbContext dbContext, ILogger<CleanupProductsJob> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public Task HandleAsync(CleanupProductsBefore payload, JobContext context)
    {
        return Task.CompletedTask;
    }
}
