using System.Data;
using Atomizer.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Atomizer.EntityFrameworkCore.Storage;

/// <summary>
/// Wraps a database transaction as a lock mechanism to fit into Atomizer's locking abstraction.
/// </summary>
/// <typeparam name="TDbContext">The type of the DbContext.</typeparam>
public class DatabaseTransactionLock<TDbContext> : IAtomizerLock
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;

    private IDbContextTransaction? _dbTransaction;
    private CancellationToken? _cancellationToken;

    public DatabaseTransactionLock(TDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Dispose()
    {
        try
        {
            _dbTransaction?.Commit();
        }
        catch
        {
            _dbTransaction?.Rollback();
            throw;
        }
        finally
        {
            _dbTransaction?.Dispose();
            _dbContext.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_dbTransaction != null)
            {
                await _dbTransaction.CommitAsync(_cancellationToken ?? CancellationToken.None);
            }
        }
        catch
        {
            if (_dbTransaction != null)
            {
                await _dbTransaction.RollbackAsync(_cancellationToken ?? CancellationToken.None);
            }
            throw;
        }
        finally
        {
            if (_dbTransaction != null)
            {
                await _dbTransaction.DisposeAsync();
            }

            await _dbContext.DisposeAsync();
        }
    }

    public async Task AcquireAsync(CancellationToken cancellationToken)
    {
        try
        {
            _dbTransaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken
            );
            Acquired = true;
        }
        catch
        {
            Acquired = false;
        }

        _cancellationToken = cancellationToken;
    }

    public bool Acquired { get; private set; }
}
