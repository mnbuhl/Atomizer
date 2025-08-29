using Atomizer.EntityFrameworkCore.Providers;
using Atomizer.EntityFrameworkCore.Tests.TestSetup.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Tests.Fixtures;

[CollectionDefinition(nameof(SqliteDatabaseFixture))]
public class SqliteDatabaseFixture : ICollectionFixture<SqliteDatabaseFixture>, IAsyncLifetime
{
    public SqliteDbContext DbContext { get; private set; } = null!;
    private string _databaseName = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _databaseName = $"atomizer_db_{Guid.NewGuid():N}.db";
        RelationalProviderCache.ResetInstanceForTests();

        DbContext = ConfigureDbContext();
        await DbContext.Database.MigrateAsync();
    }

    private SqliteDbContext ConfigureDbContext()
    {
        var options = new DbContextOptionsBuilder<SqliteDbContext>();
        options.UseSqlite($"Data Source={_databaseName}");

        return new SqliteDbContext(options.Options);
    }

    public SqliteDbContext CreateNewDbContext()
    {
        return ConfigureDbContext();
    }

    public async ValueTask DisposeAsync()
    {
        await DbContext.Database.EnsureDeletedAsync();
        await DbContext.DisposeAsync();
    }
}
