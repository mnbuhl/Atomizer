using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atomizer.EntityFrameworkCore.Tests.TestSetup.Oracle;

public class OracleDesignTimeDbContextFactory : IDesignTimeDbContextFactory<OracleDbContext>
{
    public OracleDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OracleDbContext>();
        optionsBuilder.UseOracle();

        return new OracleDbContext(optionsBuilder.Options);
    }
}
