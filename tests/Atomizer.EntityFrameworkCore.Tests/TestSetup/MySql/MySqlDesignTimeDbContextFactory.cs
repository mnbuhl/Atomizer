using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atomizer.EntityFrameworkCore.Tests.TestSetup.MySql;

public class MySqlDesignTimeDbContextFactory : IDesignTimeDbContextFactory<MySqlDbContext>
{
    public MySqlDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseMySql(new MySqlServerVersion(new Version(9, 0)));

        return new MySqlDbContext(optionsBuilder.Options);
    }
}
