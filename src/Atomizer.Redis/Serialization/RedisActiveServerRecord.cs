namespace Atomizer.Redis.Serialization;

internal sealed class RedisActiveServerRecord
{
    public string InstanceId { get; set; } = string.Empty;

    public DateTimeOffset LastHeartbeatAt { get; set; }

    public static RedisActiveServerRecord FromServer(AtomizerActiveServer server) =>
        new RedisActiveServerRecord { InstanceId = server.InstanceId, LastHeartbeatAt = server.LastHeartbeatAt };

    public AtomizerActiveServer ToServer() =>
        new AtomizerActiveServer { InstanceId = InstanceId, LastHeartbeatAt = LastHeartbeatAt };
}
