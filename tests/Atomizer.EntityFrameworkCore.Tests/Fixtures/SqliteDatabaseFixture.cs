using Atomizer.EntityFrameworkCore.Providers;
using Atomizer.EntityFrameworkCore.Tests.TestSetup.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Tests.Fixtures;

[CollectionDefinition(nameof(SqliteDatabaseFixture))]
public class SqliteDatabaseFixture : ICollectionFixture<SqliteDatabaseFixture>, IAsyncLifetime
{
    public SqliteDbContext DbContext { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        DeleteDatabaseFiles();

        var options = new DbContextOptionsBuilder<SqliteDbContext>();
        options.UseSqlite("Data Source=atomizer_test.db");

        DbContext = new SqliteDbContext(options.Options, "Atomizer");
        await DbContext.Database.MigrateAsync();

        RelationalProviderCache.ResetInstanceForTests();
    }

    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();

        DeleteDatabaseFiles();
    }

    private void DeleteDatabaseFiles()
    {
        var sqliteFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), "atomizer_test.db*");
        foreach (var file in sqliteFiles)
        {
            File.Delete(file);
        }
    }
}
