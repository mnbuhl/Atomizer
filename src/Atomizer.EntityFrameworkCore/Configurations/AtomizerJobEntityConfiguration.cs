using Atomizer.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atomizer.EntityFrameworkCore.Configurations;

/// <summary>
/// Entity Framework Core configuration for <see cref="AtomizerJobEntity"/>.
/// </summary>
public class AtomizerJobEntityConfiguration : IEntityTypeConfiguration<AtomizerJobEntity>
{
    private readonly string? _schema;

    /// <summary>
    /// Initializes a new <see cref="AtomizerJobEntityConfiguration"/> with the specified database schema.
    /// </summary>
    /// <param name="schema">The database schema to use for the jobs table, or <see langword="null"/> for the default schema.</param>
    public AtomizerJobEntityConfiguration(string? schema)
    {
        _schema = schema;
    }

    /// <summary>
    /// Configures the <see cref="AtomizerJobEntity"/> type mapping.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<AtomizerJobEntity> builder)
    {
        builder.ToTable("AtomizerJobs", _schema);
        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).ValueGeneratedOnAdd();
        builder.Property(job => job.QueueKey).IsRequired().HasMaxLength(512);
        builder.Property(job => job.PayloadType).IsRequired().HasMaxLength(1024);
        builder.Property(job => job.Payload).IsRequired();
        builder.Property(job => job.ScheduledAt).IsRequired();
        builder.Property(job => job.VisibleAt).IsRequired(false);
        builder.Property(job => job.Status).IsRequired();
        builder.Property(job => job.Attempts).IsRequired();
        builder.Property(job => job.CreatedAt).IsRequired();
        builder.Property(job => job.CompletedAt).IsRequired(false);
        builder.Property(job => job.FailedAt).IsRequired(false);
        builder.Property(job => job.LeaseToken).HasMaxLength(512);
        builder.Property(job => job.ScheduleJobKey).HasMaxLength(512);
        builder.Property(job => job.IdempotencyKey).HasMaxLength(512);
        builder.Property(job => job.UpdatedAt).IsRequired();
        builder
            .Property(job => job.RetryIntervals)
            .IsRequired()
            .HasMaxLength(4096)
            .HasConversion(
                v => string.Join(';', v.Select(ts => (long)ts.TotalMilliseconds)),
                v =>
                    v.Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => TimeSpan.FromMilliseconds(long.Parse(s)))
                        .ToArray(),
                new ValueComparer<TimeSpan[]>(
                    (c1, c2) => c1!.SequenceEqual(c2!),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToArray()
                )
            );
        builder.Property(job => job.PartitionKey).HasMaxLength(255).IsRequired(false);
        builder.Property(job => job.SequenceNumber).IsRequired(false);
        builder
            .HasIndex(job => job.IdempotencyKey)
            .IsUnique()
            .HasFilter($"{nameof(AtomizerJobEntity.IdempotencyKey)} IS NOT NULL");
    }
}
