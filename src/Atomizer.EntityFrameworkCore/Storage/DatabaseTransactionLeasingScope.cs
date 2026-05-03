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

    public DatabaseTransactionLeasingScope(IDbContextTransaction? transaction)
    {
        _transaction = transaction;
        Acquired = transaction != null;
    }

    public bool Acquired { get; }

    public void Dispose()
    {
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
