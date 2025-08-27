using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Tests.TestSetup.Postgres;

public class PostgresDbContext : TestDbContext
{
    public PostgresDbContext(DbContextOptions<TestDbContext> options, string? schema = null)
        : base(options, schema) { }
}
