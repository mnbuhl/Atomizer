namespace Atomizer;

/// <summary>
/// Represents the last known heartbeat for an Atomizer process instance.
/// </summary>
public sealed class AtomizerActiveServer
{
    /// <summary>
    /// Gets or sets the process instance identifier that owns the heartbeat.
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC time at which the instance last refreshed its heartbeat.
    /// </summary>
    public DateTimeOffset LastHeartbeatAt { get; set; }
}
