using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Tests.TestSetup;

public abstract class TestDbContext : DbContext
{
    private readonly string? _schema;

    protected TestDbContext(DbContextOptions options, string? schema = null)
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
