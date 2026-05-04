using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.EntityFrameworkCore.Storage;
using Atomizer.EntityFrameworkCore.Tests.Fixtures;
using Atomizer.EntityFrameworkCore.Tests.TestSetup.MySql;
using Atomizer.Tests.Utilities.StorageContract;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Atomizer.EntityFrameworkCore.Tests.Storage.MySql;

[Collection(nameof(MySqlDatabaseFixture))]
public sealed class MySqlStorageContractTests(MySqlDatabaseFixture fixture) : AtomizerStorageContractTests
{
    private MySqlDbContext? _dbContext;

    protected override IAtomizerStorage CreateStorage(IAtomizerClock clock)
    {
        _dbContext = fixture.CreateNewDbContext();
        return new EntityFrameworkCoreStorage<MySqlDbContext>(
            _dbContext,
            new EntityFrameworkCoreJobStorageOptions(),
            Substitute.For<ILogger<EntityFrameworkCoreStorage<MySqlDbContext>>>(),
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
