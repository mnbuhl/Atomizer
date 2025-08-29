using Atomizer.EntityFrameworkCore.Tests.TestSetup.Oracle;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Oracle;

namespace Atomizer.EntityFrameworkCore.Tests.Fixtures;

[CollectionDefinition(nameof(OracleDatabaseFixture))]
public class OracleDatabaseFixture : BaseDatabaseFixture<OracleDbContext>, ICollectionFixture<OracleDatabaseFixture>
{
    public OracleDatabaseFixture()
        : base(
            new OracleBuilder()
                .WithImage("gvenzl/oracle-free:slim-faststart")
                .WithPassword("FREE")
                .WithEnvironment("ORACLE_PDB", "FREEPDB1")
                .Build()
        ) { }

    protected override OracleDbContext ConfigureDbContext()
    {
        var connectionString =
            $"Data Source={DatabaseContainer.Hostname}:{DatabaseContainer.GetMappedPublicPort(1521)}/FREEPDB1;User Id=oracle;Password=FREE;Pooling=false;";
        var optionsBuilder = new DbContextOptionsBuilder<OracleDbContext>();
        optionsBuilder.UseOracle(connectionString);

        var dbContext = new OracleDbContext(optionsBuilder.Options);

        return dbContext;
    }
}
