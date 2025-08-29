using Atomizer.Abstractions;
using Atomizer.Locking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Atomizer.EntityFrameworkCore.Storage;

public class DatabaseTransactionLeasingScopeFactory<TDbContext> : IAtomizerLeasingScopeFactory
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly ILogger<DatabaseTransactionLeasingScopeFactory<TDbContext>> _logger;

    public DatabaseTransactionLeasingScopeFactory(
        TDbContext dbContext,
        ILogger<DatabaseTransactionLeasingScopeFactory<TDbContext>> logger
    )
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IAtomizerLeasingScope> CreateScopeAsync(
        QueueKey key,
        TimeSpan scopeTimeout,
        CancellationToken cancellationToken
    )
    {
        _logger.LogDebug("Starting database transaction leasing scope for queue {QueueKey}", key);

        if (_dbContext.Database.IsRelational())
        {
            return await DatabaseTransactionLeasingScope.StartTransaction(_dbContext, scopeTimeout, cancellationToken);
        }

        var noopLeasingScopeFactory = new NoopLeasingScopeFactory();
        return await noopLeasingScopeFactory.CreateScopeAsync(key, scopeTimeout, cancellationToken);
    }
}
