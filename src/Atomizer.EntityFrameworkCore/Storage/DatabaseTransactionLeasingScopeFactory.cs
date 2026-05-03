using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Atomizer.EntityFrameworkCore.Storage;

/// <summary>
/// Creates <see cref="DatabaseTransactionLeasingScope"/> instances for the specified <typeparamref name="TDbContext"/>.
/// </summary>
/// <typeparam name="TDbContext">The <see cref="DbContext"/> type used to begin transactions.</typeparam>
public class DatabaseTransactionLeasingScopeFactory<TDbContext>
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly ILogger<DatabaseTransactionLeasingScopeFactory<TDbContext>> _logger;

    /// <summary>
    /// Initializes a new <see cref="DatabaseTransactionLeasingScopeFactory{TDbContext}"/> with the required dependencies.
    /// </summary>
    /// <param name="dbContext">The database context used to start transactions.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public DatabaseTransactionLeasingScopeFactory(
        TDbContext dbContext,
        ILogger<DatabaseTransactionLeasingScopeFactory<TDbContext>> logger
    )
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Creates a <see cref="DatabaseTransactionLeasingScope"/> for the specified queue key.
    /// </summary>
    /// <param name="key">The queue key identifying the lease boundary.</param>
    /// <param name="scopeTimeout">Maximum time to wait when acquiring the transaction.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A new <see cref="DatabaseTransactionLeasingScope"/> wrapping the acquired transaction.</returns>
    public async Task<DatabaseTransactionLeasingScope> CreateScopeAsync(
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

        _logger.LogDebug(
            "Database is not relational, leasing scope not supported for queue {QueueKey}",
            key
        );

        throw new NotSupportedException(
            "DatabaseTransactionLeasingScopeFactory requires a relational database provider."
        );
    }
}
