namespace Atomizer.Dashboard.Contracts;

internal sealed class JobDetailDto
{
    public Guid Id { get; init; }
    public string QueueKey { get; init; } = string.Empty;
    public string PayloadTypeName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int Attempts { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset ScheduledAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset? FailedAt { get; init; }
    public string? PartitionKey { get; init; }
    public long? SequenceNumber { get; init; }
    public string Payload { get; init; } = string.Empty;
    public IReadOnlyList<JobErrorDto> Errors { get; init; } = new List<JobErrorDto>();

    internal static JobDetailDto FromDetail(AtomizerJob job) =>
        new()
        {
            Id = job.Id,
            QueueKey = job.QueueKey.ToString(),
            PayloadTypeName = job.PayloadType?.Name ?? string.Empty,
            Status = job.Status.ToString(),
            Attempts = job.Attempts,
            CreatedAt = job.CreatedAt,
            UpdatedAt = job.UpdatedAt,
            ScheduledAt = job.ScheduledAt,
            CompletedAt = job.CompletedAt,
            FailedAt = job.FailedAt,
            PartitionKey = job.PartitionKey?.ToString(),
            SequenceNumber = job.SequenceNumber,
            Payload = job.Payload,
            Errors = job.Errors.Select(JobErrorDto.From).OrderBy(e => e.Attempt).ToList(),
        };
}
