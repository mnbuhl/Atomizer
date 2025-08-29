using Atomizer.EntityFrameworkCore.Providers;
using Atomizer.EntityFrameworkCore.Tests.TestSetup.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Tests.Fixtures;

[CollectionDefinition(nameof(SqliteDatabaseFixture))]
public class SqliteDatabaseFixture : ICollectionFixture<SqliteDatabaseFixture>, IAsyncLifetime
{
    public SqliteDbContext DbContext { get; private set; } = null!;

    private string DatabaseName => $"atomizer_db_{Guid.NewGuid():N}.db";

    public async ValueTask InitializeAsync()
    {
        RelationalProviderCache.ResetInstanceForTests();
        DeleteDatabaseFiles();

        DbContext = ConfigureDbContext();
        await DbContext.Database.MigrateAsync();
    }

    private SqliteDbContext ConfigureDbContext()
    {
        var options = new DbContextOptionsBuilder<SqliteDbContext>();
        options.UseSqlite($"Data Source={DatabaseName}");

        return new SqliteDbContext(options.Options, "Atomizer");
    }

    public SqliteDbContext CreateNewDbContext()
    {
        return ConfigureDbContext();
    }

    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();

        DeleteDatabaseFiles();
    }

    private void DeleteDatabaseFiles()
    {
        var sqliteFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), $"{DatabaseName}*");
        foreach (var file in sqliteFiles)
        {
            File.Delete(file);
        }
    }
}
