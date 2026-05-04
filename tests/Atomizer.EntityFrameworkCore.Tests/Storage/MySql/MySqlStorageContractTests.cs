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
        {
            _dbContext.Set<AtomizerJobEntity>().RemoveRange(_dbContext.Set<AtomizerJobEntity>());
            _dbContext.Set<AtomizerJobErrorEntity>().RemoveRange(_dbContext.Set<AtomizerJobErrorEntity>());
            _dbContext.Set<AtomizerScheduleEntity>().RemoveRange(_dbContext.Set<AtomizerScheduleEntity>());
            await _dbContext.SaveChangesAsync();
            await _dbContext.DisposeAsync();
        }
    }
}
