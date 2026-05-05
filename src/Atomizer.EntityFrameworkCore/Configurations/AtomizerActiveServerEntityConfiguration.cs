using Atomizer.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atomizer.EntityFrameworkCore.Configurations;

/// <summary>
/// Entity Framework Core configuration for <see cref="AtomizerActiveServerEntity"/>.
/// </summary>
public class AtomizerActiveServerEntityConfiguration : IEntityTypeConfiguration<AtomizerActiveServerEntity>
{
    private readonly string? _schema;

    /// <summary>
    /// Initializes a new <see cref="AtomizerActiveServerEntityConfiguration"/> with the specified database schema.
    /// </summary>
    public AtomizerActiveServerEntityConfiguration(string? schema)
    {
        _schema = schema;
    }

    /// <summary>
    /// Configures the <see cref="AtomizerActiveServerEntity"/> type mapping.
    /// </summary>
    public void Configure(EntityTypeBuilder<AtomizerActiveServerEntity> builder)
    {
        builder.ToTable("AtomizerActiveServers", _schema);
        builder.HasKey(server => server.InstanceId);
        builder.Property(server => server.InstanceId).IsRequired().HasMaxLength(512).ValueGeneratedNever();
        builder.Property(server => server.LastHeartbeatAt).IsRequired();
        builder
            .HasIndex(server => new { server.LastHeartbeatAt, server.InstanceId })
            .HasDatabaseName("IX_AtomizerActiveServers_LastHeartbeatAt_InstanceId");
    }
}
