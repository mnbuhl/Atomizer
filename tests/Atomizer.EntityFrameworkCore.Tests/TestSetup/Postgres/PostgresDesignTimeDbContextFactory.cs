using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atomizer.EntityFrameworkCore.Tests.TestSetup.Postgres;

public class PostgresDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PostgresDbContext>
{
    public PostgresDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PostgresDbContext>();
        // optionsBuilder.UseNpgsql(options =>
        // {
        //     options.MigrationsAssembly(typeof(PostgresDbContextModelSnapshot).AssemblyQualifiedName);
        // });
        optionsBuilder.EnableDetailedErrors();
        optionsBuilder.EnableSensitiveDataLogging();

        return new PostgresDbContext(optionsBuilder.Options, "Atomizer");
    }
}
