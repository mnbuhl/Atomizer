using Atomizer.EntityFrameworkCore.Tests.TestSetup.Oracle;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Oracle;

namespace Atomizer.EntityFrameworkCore.Tests.Fixtures;

[CollectionDefinition(nameof(OracleDatabaseFixture))]
public class OracleDatabaseFixture : BaseDatabaseFixture<OracleDbContext>, ICollectionFixture<OracleDatabaseFixture>
{
    public OracleDatabaseFixture()
        : base(new OracleBuilder().WithUsername("test_user").WithImage("gvenzl/oracle-free:23-slim-faststart").Build())
    { }

    protected override OracleDbContext ConfigureDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<OracleDbContext>();
        optionsBuilder.UseOracle(DatabaseContainer.GetConnectionString());

        return new OracleDbContext(optionsBuilder.Options, "Atomizer");
    }
}
