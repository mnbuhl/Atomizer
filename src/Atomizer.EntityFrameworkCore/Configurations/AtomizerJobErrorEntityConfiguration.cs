using Atomizer.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atomizer.EntityFrameworkCore.Configurations;

/// <summary>
/// Entity Framework Core configuration for <see cref="AtomizerJobErrorEntity"/>.
/// </summary>
public class AtomizerJobErrorEntityConfiguration : IEntityTypeConfiguration<AtomizerJobErrorEntity>
{
    private readonly string? _schema;

    /// <summary>
    /// Initializes a new <see cref="AtomizerJobErrorEntityConfiguration"/> with the specified database schema.
    /// </summary>
    /// <param name="schema">The database schema to use for the job errors table, or <see langword="null"/> for the default schema.</param>
    public AtomizerJobErrorEntityConfiguration(string? schema)
    {
        _schema = schema;
    }

    /// <summary>
    /// Configures the <see cref="AtomizerJobErrorEntity"/> type mapping.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<AtomizerJobErrorEntity> builder)
    {
        builder.ToTable("AtomizerJobErrors", _schema);
        builder.HasKey(error => error.Id);
        builder.Property(error => error.Id).ValueGeneratedOnAdd();
        builder.Property(error => error.JobId).IsRequired();
        builder.Property(error => error.ErrorMessage).HasMaxLength(2048);
        builder.Property(error => error.StackTrace).HasMaxLength(5120);
        builder.Property(error => error.ExceptionType).HasMaxLength(1024);
        builder.Property(error => error.CreatedAt).IsRequired();
        builder.Property(error => error.Attempt).IsRequired();
        builder.Property(error => error.RuntimeIdentity).HasMaxLength(255);

        // Configure the relationship with AtomizerJobEntity
        builder
            .HasOne(error => error.Job)
            .WithMany(job => job!.Errors)
            .HasForeignKey(error => error.JobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
