using Atomizer.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.FlowTests.Infrastructure;

public sealed class FlowTestDbContext : DbContext
{
    private readonly string? _schema;

    public FlowTestDbContext(DbContextOptions<FlowTestDbContext> options, string? schema = null)
        : base(options)
    {
        _schema = schema;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddAtomizerEntities(_schema);
        base.OnModelCreating(modelBuilder);
    }
}
