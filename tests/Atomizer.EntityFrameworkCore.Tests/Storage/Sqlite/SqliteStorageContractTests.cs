using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.EntityFrameworkCore.Storage;
using Atomizer.EntityFrameworkCore.Tests.Fixtures;
using Atomizer.EntityFrameworkCore.Tests.TestSetup.Sqlite;
using Atomizer.Tests.Utilities.StorageContract;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Atomizer.EntityFrameworkCore.Tests.Storage.Sqlite;

/// <summary>
/// Contract tests for <see cref="EntityFrameworkCoreStorage{TDbContext}"/> backed by SQLite.
/// SQLite is not a supported production provider; it exercises the LINQ fallback path
/// (<c>AllowUnsafeProviderFallback = true</c>), not the CTE dialect SQL.
/// The CTE dialect SQL is verified by the PostgreSQL, SQL Server, and MySQL subclasses.
/// </summary>
/// <remarks>
/// NOTE: The two FIFO-08 partition-blocking tests
/// (<c>GetDueJobsAsync_WhenPartitionIsBlockedByProcessing_ShouldExcludeEntirePartition</c> and
/// <c>GetDueJobsAsync_WhenPartitionIsBlockedByPendingWithAttempts_ShouldExcludeEntirePartition</c>)
/// are expected to FAIL for SQLite. The LINQ fallback path in <c>GetDueJobsAsync</c> does not
/// enforce FIFO partition blocking — it returns all due jobs without partition exclusion. This is a
/// known limitation of the LINQ fallback; FIFO enforcement requires the provider-specific CTE SQL
/// implemented in PostgreSqlDialect, SqlServerDialect, and MySqlDialect. The real providers are
/// the authoritative FIFO test surface.
/// </remarks>
[Collection(nameof(SqliteDatabaseFixture))]
public sealed class SqliteStorageContractTests(SqliteDatabaseFixture fixture) : AtomizerStorageContractTests
{
    private SqliteDbContext? _dbContext;

    protected override IAtomizerStorage CreateStorage(IAtomizerClock clock)
    {
        _dbContext = fixture.CreateNewDbContext();
        return new EntityFrameworkCoreStorage<SqliteDbContext>(
            _dbContext,
            new EntityFrameworkCoreJobStorageOptions { AllowUnsafeProviderFallback = true },
            Substitute.For<ILogger<EntityFrameworkCoreStorage<SqliteDbContext>>>(),
            clock
        );
    }

    public override async ValueTask DisposeAsync()
    {
        if (_dbContext is not null)
            await _dbContext.DisposeAsync();

        // Delete errors before jobs to satisfy the FK constraint, then schedules.
        // Use a bounded cancellation token so teardown does not hang indefinitely.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var cleanupContext = fixture.CreateNewDbContext();
        cleanupContext.Set<AtomizerJobErrorEntity>().RemoveRange(cleanupContext.Set<AtomizerJobErrorEntity>());
        cleanupContext.Set<AtomizerJobEntity>().RemoveRange(cleanupContext.Set<AtomizerJobEntity>());
        cleanupContext.Set<AtomizerScheduleEntity>().RemoveRange(cleanupContext.Set<AtomizerScheduleEntity>());
        await cleanupContext.SaveChangesAsync(cts.Token);
    }
}
