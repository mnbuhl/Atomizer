namespace Atomizer.Dashboard.Contracts;

internal sealed class JobActionResponse
{
    public Guid JobId { get; init; }
    public Guid? SourceJobId { get; init; }
    public string Status { get; init; } = string.Empty;

    internal static JobActionResponse From(AtomizerJob job, Guid? sourceJobId = null) =>
        new()
        {
            JobId = job.Id,
            SourceJobId = sourceJobId,
            Status = job.Status.ToString(),
        };
}

internal sealed class ScheduleActionResponse
{
    public ScheduleDto Schedule { get; init; } = new();

    internal static ScheduleActionResponse From(AtomizerSchedule schedule) =>
        new() { Schedule = ScheduleDto.From(schedule) };
}

internal sealed class JobTypeOptionDto
{
    public string Id { get; init; } = string.Empty;
    public string PayloadTypeName { get; init; } = string.Empty;
    public string PayloadTypeFullName { get; init; } = string.Empty;
}

internal sealed class SetScheduleEnabledRequest
{
    public bool Enabled { get; init; }
}

internal sealed class TriggerJobRequest
{
    public string PayloadTypeId { get; init; } = string.Empty;
    public string QueueKey { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
}
