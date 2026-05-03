using Atomizer.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atomizer.EntityFrameworkCore.Configurations;

/// <summary>
/// Entity Framework Core configuration for <see cref="AtomizerScheduleEntity"/>.
/// </summary>
public class AtomizerScheduleEntityConfiguration : IEntityTypeConfiguration<AtomizerScheduleEntity>
{
    private readonly string? _schema;

    /// <summary>
    /// Initializes a new <see cref="AtomizerScheduleEntityConfiguration"/> with the specified database schema.
    /// </summary>
    /// <param name="schema">The database schema to use for the schedules table, or <see langword="null"/> for the default schema.</param>
    public AtomizerScheduleEntityConfiguration(string? schema)
    {
        _schema = schema;
    }

    /// <summary>
    /// Configures the <see cref="AtomizerScheduleEntity"/> type mapping.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<AtomizerScheduleEntity> builder)
    {
        builder.ToTable("AtomizerSchedules", _schema);
        builder.HasKey(e => e.Id);
        builder.Property(job => job.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.JobKey).IsRequired().HasMaxLength(255);
        builder.Property(e => e.QueueKey).IsRequired().HasMaxLength(100);
        builder.Property(e => e.PayloadType).IsRequired().HasMaxLength(1024);
        builder.Property(e => e.Payload).IsRequired();
        builder.Property(e => e.Schedule).IsRequired().HasMaxLength(1024);
        builder.Property(e => e.TimeZone).IsRequired().HasMaxLength(64);
        builder.Property(e => e.MisfirePolicy).IsRequired();
        builder.Property(e => e.MaxCatchUp).IsRequired();
        builder.Property(e => e.Enabled).IsRequired();
        builder.Property(e => e.NextRunAt).IsRequired();
        builder.Property(e => e.LastEnqueueAt);
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired();
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

        builder.HasIndex(e => e.JobKey).IsUnique();
    }
}
