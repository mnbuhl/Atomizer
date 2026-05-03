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

    /// <summary>
    /// Initializes a new <see cref="DatabaseTransactionLeasingScope"/> wrapping the specified transaction.
    /// </summary>
    /// <param name="transaction">The database transaction to wrap, or <see langword="null"/> if acquisition failed.</param>
    public DatabaseTransactionLeasingScope(IDbContextTransaction? transaction)
    {
        _transaction = transaction;
        Acquired = transaction != null;
    }

    /// <summary>
    /// Gets a value indicating whether the transaction was successfully acquired.
    /// </summary>
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

    /// <summary>
    /// Commits or rolls back the transaction depending on whether <see cref="Abort"/> was called.
    /// </summary>
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

    /// <summary>
    /// Asynchronously commits or rolls back the transaction depending on whether <see cref="Abort"/> was called.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous dispose operation.</returns>
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

    /// <summary>
    /// Begins a <see cref="System.Data.IsolationLevel.ReadCommitted"/> transaction on the given context,
    /// returning a scope that wraps it. Returns a non-acquired scope if the transaction cannot be started.
    /// </summary>
    /// <typeparam name="TDbContext">The <see cref="DbContext"/> type.</typeparam>
    /// <param name="dbContext">The database context on which to begin the transaction.</param>
    /// <param name="timeout">Maximum time to wait for the transaction to start.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A <see cref="DatabaseTransactionLeasingScope"/> wrapping the started transaction.</returns>
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
