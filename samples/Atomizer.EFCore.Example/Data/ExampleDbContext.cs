using Atomizer.EFCore.Example.Entities;
using Atomizer.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EFCore.Example.Data;

public class ExampleDbContext(DbContextOptions<ExampleDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddAtomizerEntities();

        base.OnModelCreating(modelBuilder);
    }
}
