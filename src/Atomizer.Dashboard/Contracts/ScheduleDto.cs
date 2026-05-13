namespace Atomizer.Dashboard.Contracts;

internal sealed class ScheduleDto
{
    public Guid Id { get; init; }
    public string JobKey { get; init; } = string.Empty;
    public string QueueKey { get; init; } = string.Empty;
    public string PayloadTypeName { get; init; } = string.Empty;
    public string Cron { get; init; } = string.Empty;
    public DateTimeOffset NextRunAt { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
    public bool Enabled { get; init; }
    public string MisfirePolicy { get; init; } = string.Empty;

    internal static ScheduleDto From(AtomizerSchedule s) =>
        new()
        {
            Id = s.Id,
            JobKey = s.JobKey.ToString(),
            QueueKey = s.QueueKey.ToString(),
            PayloadTypeName = s.PayloadType?.Name ?? string.Empty,
            Cron = s.Schedule.ToString(),
            NextRunAt = s.NextRunAt,
            LastRunAt = s.LastEnqueueAt,
            Enabled = s.Enabled,
            MisfirePolicy = s.MisfirePolicy.ToString(),
        };
}
