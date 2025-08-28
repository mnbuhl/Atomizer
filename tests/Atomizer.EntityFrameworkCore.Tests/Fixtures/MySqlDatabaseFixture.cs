using Atomizer.EntityFrameworkCore.Tests.TestSetup.MySql;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MySql;

namespace Atomizer.EntityFrameworkCore.Tests.Fixtures;

[CollectionDefinition(nameof(MySqlDatabaseFixture))]
public class MySqlDatabaseFixture : BaseDatabaseFixture<MySqlDbContext>, ICollectionFixture<MySqlDatabaseFixture>
{
    public MySqlDatabaseFixture()
        : base(
            new MySqlBuilder()
                .WithDatabase("atomizer")
                .WithUsername("root")
                .WithPassword("secret")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(3306))
                .Build()
        ) { }

    protected override MySqlDbContext ConfigureDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<MySqlDbContext>();
        optionsBuilder.UseMySql(
            DatabaseContainer.GetConnectionString(),
            ServerVersion.AutoDetect(DatabaseContainer.GetConnectionString())
        );

        return new MySqlDbContext(optionsBuilder.Options);
    }
}
