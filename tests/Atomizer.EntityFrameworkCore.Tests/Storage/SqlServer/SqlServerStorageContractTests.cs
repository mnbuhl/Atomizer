using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.EntityFrameworkCore.Storage;
using Atomizer.EntityFrameworkCore.Tests.Fixtures;
using Atomizer.EntityFrameworkCore.Tests.TestSetup.SqlServer;
using Atomizer.Tests.Utilities.StorageContract;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Atomizer.EntityFrameworkCore.Tests.Storage.SqlServer;

[Collection(nameof(SqlServerDatabaseFixture))]
public sealed class SqlServerStorageContractTests(SqlServerDatabaseFixture fixture) : AtomizerStorageContractTests
{
    private SqlServerDbContext? _dbContext;

    protected override IAtomizerStorage CreateStorage(IAtomizerClock clock)
    {
        _dbContext = fixture.CreateNewDbContext();
        return new EntityFrameworkCoreStorage<SqlServerDbContext>(
            _dbContext,
            new EntityFrameworkCoreJobStorageOptions(),
            Substitute.For<ILogger<EntityFrameworkCoreStorage<SqlServerDbContext>>>(),
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
        await StorageTestCleanup.ClearAsync(cleanupContext, cts.Token);
    }
}
