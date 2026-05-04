using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.EntityFrameworkCore.Entities;
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
        {
            await _dbContext.DisposeAsync();
        }

        await using var cleanupContext = fixture.CreateNewDbContext();
        cleanupContext.Set<AtomizerJobEntity>().RemoveRange(cleanupContext.Set<AtomizerJobEntity>());
        cleanupContext.Set<AtomizerJobErrorEntity>().RemoveRange(cleanupContext.Set<AtomizerJobErrorEntity>());
        cleanupContext.Set<AtomizerScheduleEntity>().RemoveRange(cleanupContext.Set<AtomizerScheduleEntity>());
        await cleanupContext.SaveChangesAsync();
    }
}
