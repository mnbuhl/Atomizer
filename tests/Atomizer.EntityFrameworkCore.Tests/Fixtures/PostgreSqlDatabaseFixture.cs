using Atomizer.EntityFrameworkCore.Tests.TestSetup;
using Atomizer.EntityFrameworkCore.Tests.TestSetup.Postgres;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Atomizer.EntityFrameworkCore.Tests.Fixtures;

[CollectionDefinition(nameof(PostgreSqlDatabaseFixture))]
public class PostgreSqlDatabaseFixture
    : BaseDatabaseFixture<PostgresDbContext>,
        ICollectionFixture<PostgreSqlDatabaseFixture>
{
    public PostgreSqlDatabaseFixture()
        : base(
            new PostgreSqlBuilder()
                .WithDatabase("atomizer")
                .WithUsername("postgres")
                .WithPassword("secret")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
                .Build()
        ) { }

    protected override async Task<PostgresDbContext> InitializeDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<PostgresDbContext>();
        optionsBuilder.UseNpgsql(DatabaseContainer.GetConnectionString());

        var context = new PostgresDbContext(optionsBuilder.Options, "Atomizer");

        await context.Database.MigrateAsync();
        return context;
    }
}
