using Atomizer.EntityFrameworkCore.Tests.TestSetup;
using Atomizer.EntityFrameworkCore.Tests.TestSetup.Postgres;
using DotNet.Testcontainers.Builders;
using Testcontainers.PostgreSql;

namespace Atomizer.EntityFrameworkCore.Tests.Fixtures;

[CollectionDefinition(nameof(PostgreSqlDatabaseFixture))]
public class PostgreSqlDatabaseFixture : BaseDatabaseFixture, ICollectionFixture<PostgreSqlDatabaseFixture>
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

    protected override Task<PostgresDbContext> InitializeDbContext()
    {
        throw new NotImplementedException();
    }
}
