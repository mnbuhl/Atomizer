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
            _dbContext.Set<AtomizerJobEntity>().RemoveRange(_dbContext.Set<AtomizerJobEntity>());
            _dbContext.Set<AtomizerJobErrorEntity>().RemoveRange(_dbContext.Set<AtomizerJobErrorEntity>());
            _dbContext.Set<AtomizerScheduleEntity>().RemoveRange(_dbContext.Set<AtomizerScheduleEntity>());
            await _dbContext.SaveChangesAsync();
            await _dbContext.DisposeAsync();
        }
    }
}
