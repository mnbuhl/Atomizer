namespace Atomizer.Dashboard.Contracts;

internal sealed class ServerDto
{
    public string InstanceId { get; init; } = string.Empty;
    public DateTimeOffset LastHeartbeatAt { get; init; }
    public int AgeSeconds { get; init; }

    internal static ServerDto From(AtomizerActiveServer server, DateTimeOffset now) =>
        new()
        {
            InstanceId = server.InstanceId,
            LastHeartbeatAt = server.LastHeartbeatAt,
            AgeSeconds = (int)(now - server.LastHeartbeatAt).TotalSeconds,
        };
}
