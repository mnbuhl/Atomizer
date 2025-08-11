using Atomizer.EFCore.Example.Data;

namespace Atomizer.EFCore.Example.Handlers;

public record CleanupProductsBefore(DateTime BeforeDate);

public class CleanupProductsJobHandler : IAtomizerJobHandler<CleanupProductsBefore>
{
    private readonly ExampleDbContext _dbContext;
    private readonly ILogger<CleanupProductsJobHandler> _logger;

    public CleanupProductsJobHandler(ExampleDbContext dbContext, ILogger<CleanupProductsJobHandler> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task HandleAsync(CleanupProductsBefore payload, JobContext context) { }
}
