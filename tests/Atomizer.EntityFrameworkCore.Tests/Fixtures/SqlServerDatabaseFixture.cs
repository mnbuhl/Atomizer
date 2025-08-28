using Atomizer.EntityFrameworkCore.Tests.TestSetup.SqlServer;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Atomizer.EntityFrameworkCore.Tests.Fixtures;

[CollectionDefinition(nameof(SqlServerDatabaseFixture))]
public class SqlServerDatabaseFixture
    : BaseDatabaseFixture<SqlServerDbContext>,
        ICollectionFixture<SqlServerDatabaseFixture>
{
    public SqlServerDatabaseFixture()
        : base(new MsSqlBuilder().Build()) { }

    protected override SqlServerDbContext ConfigureDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<SqlServerDbContext>();
        optionsBuilder.UseSqlServer(DatabaseContainer.GetConnectionString());

        return new SqlServerDbContext(optionsBuilder.Options, "Atomizer");
    }
}
