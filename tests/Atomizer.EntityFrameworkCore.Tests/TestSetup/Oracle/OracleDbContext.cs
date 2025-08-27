using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Tests.TestSetup.Oracle;

public class OracleDbContext : TestDbContext
{
    public OracleDbContext(DbContextOptions<TestDbContext> options, string? schema = null)
        : base(options, schema) { }
}
