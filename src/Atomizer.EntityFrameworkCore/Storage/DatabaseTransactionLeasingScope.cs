using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Atomizer.EntityFrameworkCore.Storage;

/// <summary>
/// Wraps a database transaction as a lock mechanism for Atomizer's leasing abstraction.
/// </summary>
public class DatabaseTransactionLeasingScope : IDisposable, IAsyncDisposable
{
    private readonly IDbContextTransaction? _transaction;
    private bool _aborted;

    public DatabaseTransactionLeasingScope(IDbContextTransaction? transaction)
    {
        _transaction = transaction;
        Acquired = transaction != null;
    }

    public bool Acquired { get; }

    /// <summary>
    /// Marks the scope as aborted so that <see cref="Dispose"/> and <see cref="DisposeAsync"/>
    /// roll back the transaction instead of committing it.
    /// Call this in a catch block before rethrowing when the lease body threw an exception.
    /// </summary>
    public void Abort()
    {
        _aborted = true;
    }

    public void Dispose()
    {
        if (_aborted)
        {
            try
            {
                _transaction?.Rollback();
            }
            finally
            {
                _transaction?.Dispose();
            }

            return;
        }

        try
        {
            _transaction?.Commit();
        }
        catch
        {
            _transaction?.Rollback();
            throw;
        }
        finally
        {
            _transaction?.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_aborted)
        {
            try
            {
                if (_transaction != null)
                {
                    await _transaction.RollbackAsync();
                }
            }
            finally
            {
                if (_transaction != null)
                {
                    await _transaction.DisposeAsync();
                }
            }

            return;
        }

        try
        {
            if (_transaction != null)
            {
                await _transaction.CommitAsync();
            }
        }
        catch
        {
            if (_transaction != null)
            {
                await _transaction.RollbackAsync();
            }
            throw;
        }
        finally
        {
            if (_transaction != null)
            {
                await _transaction.DisposeAsync();
            }
        }
    }

    public static async Task<DatabaseTransactionLeasingScope> StartTransaction<TDbContext>(
        TDbContext dbContext,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
        where TDbContext : DbContext
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            return new DatabaseTransactionLeasingScope(
                await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cts.Token)
            );
        }
        catch
        {
            return new DatabaseTransactionLeasingScope(null);
        }
    }
}
