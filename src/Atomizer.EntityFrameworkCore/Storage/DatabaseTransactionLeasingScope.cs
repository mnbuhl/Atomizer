using System.Data;
using Atomizer.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Atomizer.EntityFrameworkCore.Storage;

/// <summary>
/// Wraps a database transaction as a lock mechanism to fit into Atomizer's locking abstraction.
/// </summary>
/// <typeparam name="TDbContext">The type of the DbContext.</typeparam>
public class DatabaseTransactionLeasingScope<TDbContext> : IAtomizerLeasingScope
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly TimeSpan _timeout;

    private IDbContextTransaction? _dbTransaction;

    public DatabaseTransactionLeasingScope(TDbContext dbContext, TimeSpan timeout)
    {
        _dbContext = dbContext;
        _timeout = timeout;
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
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_dbTransaction != null)
            {
                await _dbTransaction.CommitAsync();
            }
        }
        catch
        {
            if (_dbTransaction != null)
            {
                await _dbTransaction.RollbackAsync();
            }
            throw;
        }
        finally
        {
            if (_dbTransaction != null)
            {
                await _dbTransaction.DisposeAsync();
            }
        }
    }

    public async Task AcquireAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);
            _dbTransaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cts.Token);
            Acquired = true;
        }
        catch
        {
            Acquired = false;
        }
    }

    public bool Acquired { get; private set; }
}
