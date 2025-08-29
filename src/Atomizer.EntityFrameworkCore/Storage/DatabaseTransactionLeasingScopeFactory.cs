using Atomizer.Abstractions;
using Atomizer.Core;
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
        if (_dbContext.Database.IsRelational())
        {
            _logger.LogDebug("Starting database transaction leasing scope for queue {QueueKey}", key);
            return await DatabaseTransactionLeasingScope.StartTransaction(_dbContext, scopeTimeout, cancellationToken);
        }

        _logger.LogDebug("Database is not relational, using NoopLeasingScopeFactory for queue {QueueKey}", key);

        var noopLeasingScopeFactory = new NoopLeasingScopeFactory();
        return await noopLeasingScopeFactory.CreateScopeAsync(key, scopeTimeout, cancellationToken);
    }
}
