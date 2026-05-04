namespace Atomizer.EntityFrameworkCore.Entities;

/// <summary>
/// Database entity representing an Atomizer process heartbeat.
/// </summary>
public class AtomizerActiveServerEntity
{
    /// <summary>Gets or sets the process instance identifier.</summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC time of the last heartbeat.</summary>
    public DateTimeOffset LastHeartbeatAt { get; set; }
}

/// <summary>
/// Provides mapping methods between <see cref="AtomizerActiveServer"/> domain objects and EF entities.
/// </summary>
public static class AtomizerActiveServerEntityMapper
{
    /// <summary>Maps a domain active-server record to its EF entity representation.</summary>
    public static AtomizerActiveServerEntity ToEntity(this AtomizerActiveServer server) =>
        new() { InstanceId = server.InstanceId, LastHeartbeatAt = server.LastHeartbeatAt };

    /// <summary>Maps an EF active-server entity to its domain representation.</summary>
    public static AtomizerActiveServer ToAtomizerActiveServer(this AtomizerActiveServerEntity entity) =>
        new() { InstanceId = entity.InstanceId, LastHeartbeatAt = entity.LastHeartbeatAt };
}
