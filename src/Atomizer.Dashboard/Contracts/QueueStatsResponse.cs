using Atomizer;

namespace Atomizer.Dashboard.Contracts;

internal sealed class QueueStatsResponse
{
    public IReadOnlyList<QueueStatsDto> Queues { get; init; } = new List<QueueStatsDto>();
}

internal sealed class QueueStatsDto
{
    public string QueueKey { get; init; } = string.Empty;
    public int Pending { get; init; }
    public int Processing { get; init; }
    public int Completed { get; init; }
    public int Failed { get; init; }
    public int Cancelled { get; init; }

    internal static QueueStatsDto From(QueueStats s) =>
        new()
        {
            QueueKey = s.QueueKey.ToString(),
            Pending = s.Pending,
            Processing = s.Processing,
            Completed = s.Completed,
            Failed = s.Failed,
            Cancelled = s.Cancelled,
        };
}
