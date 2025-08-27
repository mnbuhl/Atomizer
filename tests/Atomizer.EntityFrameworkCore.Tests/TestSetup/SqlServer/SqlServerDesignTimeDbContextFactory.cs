using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atomizer.EntityFrameworkCore.Tests.TestSetup.SqlServer;

public class SqlServerDesignTimeDbContextFactory : IDesignTimeDbContextFactory<SqlServerDbContext>
{
    public SqlServerDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SqlServerDbContext>();
        optionsBuilder.UseSqlServer();

        return new SqlServerDbContext(optionsBuilder.Options, "Atomizer");
    }
}
