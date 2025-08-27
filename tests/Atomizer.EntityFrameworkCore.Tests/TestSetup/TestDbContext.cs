using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Tests.TestSetup;

public class TestDbContext : DbContext
{
    private readonly string? _schema;

    public TestDbContext(DbContextOptions<TestDbContext> options, string? schema = null)
        : base(options)
    {
        _schema = schema;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddAtomizerEntities(schema: _schema);
        base.OnModelCreating(modelBuilder);
    }
}
