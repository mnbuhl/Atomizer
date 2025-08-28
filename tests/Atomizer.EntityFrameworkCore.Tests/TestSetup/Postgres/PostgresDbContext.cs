using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Tests.TestSetup.Postgres;

public class PostgresDbContext : TestDbContext
{
    public PostgresDbContext(DbContextOptions<PostgresDbContext> options, string? schema = null)
        : base(options, schema) { }
}
