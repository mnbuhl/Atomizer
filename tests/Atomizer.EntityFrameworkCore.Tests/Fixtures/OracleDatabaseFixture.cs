using Atomizer.EntityFrameworkCore.Tests.TestSetup.Oracle;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Oracle;

namespace Atomizer.EntityFrameworkCore.Tests.Fixtures;

[CollectionDefinition(nameof(OracleDatabaseFixture))]
public class OracleDatabaseFixture : BaseDatabaseFixture<OracleDbContext>, ICollectionFixture<OracleDatabaseFixture>
{
    public OracleDatabaseFixture()
        : base(
            new OracleBuilder()
                .WithPassword("oracle")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("DATABASE IS READY TO USE!"))
                .Build()
        ) { }

    protected override OracleDbContext ConfigureDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<OracleDbContext>();
        optionsBuilder.UseOracle(((OracleContainer)DatabaseContainer).GetConnectionString());

        return new OracleDbContext(optionsBuilder.Options, "Atomizer");
    }
}
