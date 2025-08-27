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

    protected override PostgresDbContext ConfigureDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<PostgresDbContext>();
        optionsBuilder.UseNpgsql(DatabaseContainer.GetConnectionString());

        return new PostgresDbContext(optionsBuilder.Options, "Atomizer");
    }
}
