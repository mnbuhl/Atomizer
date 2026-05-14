using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Dashboard.Contracts;

namespace Atomizer.Dashboard.Services;

internal sealed class DashboardCommandService
{
    private readonly AtomizerOptions _atomizerOptions;
    private readonly IAtomizerClock _clock;
    private readonly IAtomizerJobSerializer _serializer;
    private readonly IAtomizerStorage _storage;

    public DashboardCommandService(
        IAtomizerStorage storage,
        IAtomizerClock clock,
        IAtomizerJobSerializer serializer,
        AtomizerOptions atomizerOptions
    )
    {
        _storage = storage;
        _clock = clock;
        _serializer = serializer;
        _atomizerOptions = atomizerOptions;
    }

    public IReadOnlyList<JobTypeOptionDto> GetJobTypeOptions()
    {
        return _atomizerOptions
            .JobHandlers.Select(handler => handler.PayloadType)
            .Where(payloadType => payloadType.AssemblyQualifiedName is not null)
            .Distinct()
            .OrderBy(payloadType => payloadType.FullName ?? payloadType.Name, StringComparer.Ordinal)
            .Select(payloadType => new JobTypeOptionDto
            {
                Id = payloadType.AssemblyQualifiedName!,
                PayloadTypeName = payloadType.Name,
                PayloadTypeFullName = payloadType.FullName ?? payloadType.Name,
            })
            .ToList();
    }

    public async Task<DashboardCommandResult<JobActionResponse>> RetryJobAsync(
        Guid jobId,
        CancellationToken cancellationToken
    )
    {
        var source = await _storage.GetJobByIdAsync(jobId, cancellationToken);
        if (source is null)
        {
            return DashboardCommandResult<JobActionResponse>.NotFound("Job was not found.");
        }

        if (source.Status != AtomizerJobStatus.Failed)
        {
            return DashboardCommandResult<JobActionResponse>.Conflict("Only failed jobs can be retried.");
        }

        if (source.PayloadType is null)
        {
            return DashboardCommandResult<JobActionResponse>.Conflict("Job has no payload type.");
        }

        var now = _clock.UtcNow;
        var retry = AtomizerJob.Create(
            source.QueueKey,
            source.PayloadType,
            source.Payload,
            now,
            now,
            source.RetryStrategy,
            scheduleJobKey: source.ScheduleJobKey,
            partitionKey: source.PartitionKey
        );

        await _storage.InsertAsync(retry, cancellationToken);
        return DashboardCommandResult<JobActionResponse>.Ok(JobActionResponse.From(retry, source.Id));
    }

    public async Task<DashboardCommandResult<JobActionResponse>> CancelJobAsync(
        Guid jobId,
        CancellationToken cancellationToken
    )
    {
        var job = await _storage.GetJobByIdAsync(jobId, cancellationToken);
        if (job is null)
        {
            return DashboardCommandResult<JobActionResponse>.NotFound("Job was not found.");
        }

        if (job.Status != AtomizerJobStatus.Pending)
        {
            return DashboardCommandResult<JobActionResponse>.Conflict("Only pending jobs can be cancelled.");
        }

        try
        {
            job.Cancel(_clock.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            return DashboardCommandResult<JobActionResponse>.Conflict(ex.Message);
        }

        await _storage.UpdateJobsAsync([job], cancellationToken);
        return DashboardCommandResult<JobActionResponse>.Ok(JobActionResponse.From(job));
    }

    public async Task<DashboardCommandResult<JobActionResponse>> TriggerJobAsync(
        TriggerJobRequest request,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(request.PayloadTypeId))
        {
            return DashboardCommandResult<JobActionResponse>.BadRequest("Payload type is required.");
        }

        if (string.IsNullOrWhiteSpace(request.QueueKey))
        {
            return DashboardCommandResult<JobActionResponse>.BadRequest("Queue key is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Payload))
        {
            return DashboardCommandResult<JobActionResponse>.BadRequest("Payload is required.");
        }

        var payloadType = _atomizerOptions
            .JobHandlers.Select(handler => handler.PayloadType)
            .FirstOrDefault(type => type.AssemblyQualifiedName == request.PayloadTypeId);

        if (payloadType is null)
        {
            return DashboardCommandResult<JobActionResponse>.BadRequest("Payload type is not registered.");
        }

        try
        {
            _serializer.Deserialize(request.Payload, payloadType);
        }
        catch (Exception ex)
        {
            return DashboardCommandResult<JobActionResponse>.BadRequest(ex.Message);
        }

        QueueKey queueKey;
        try
        {
            queueKey = new QueueKey(request.QueueKey);
        }
        catch (Exception ex)
        {
            return DashboardCommandResult<JobActionResponse>.BadRequest(ex.Message);
        }

        var now = _clock.UtcNow;
        var job = AtomizerJob.Create(queueKey, payloadType, request.Payload, now, now);
        await _storage.InsertAsync(job, cancellationToken);
        return DashboardCommandResult<JobActionResponse>.Ok(JobActionResponse.From(job));
    }

    public async Task<DashboardCommandResult<ScheduleActionResponse>> SetScheduleEnabledAsync(
        Guid scheduleId,
        bool enabled,
        CancellationToken cancellationToken
    )
    {
        var schedule = await GetScheduleAsync(scheduleId, cancellationToken);
        if (schedule is null)
        {
            return DashboardCommandResult<ScheduleActionResponse>.NotFound("Schedule was not found.");
        }

        var now = _clock.UtcNow;
        if (enabled)
        {
            schedule.Enable(now);
        }
        else
        {
            schedule.Disable(now);
        }

        await _storage.UpdateSchedulesAsync([schedule], cancellationToken);
        return DashboardCommandResult<ScheduleActionResponse>.Ok(ScheduleActionResponse.From(schedule));
    }

    public async Task<DashboardCommandResult<JobActionResponse>> RunScheduleNowAsync(
        Guid scheduleId,
        CancellationToken cancellationToken
    )
    {
        var schedule = await GetScheduleAsync(scheduleId, cancellationToken);
        if (schedule is null)
        {
            return DashboardCommandResult<JobActionResponse>.NotFound("Schedule was not found.");
        }

        if (schedule.PayloadType is null)
        {
            return DashboardCommandResult<JobActionResponse>.Conflict("Schedule has no payload type.");
        }

        var now = _clock.UtcNow;
        var job = AtomizerJob.Create(
            schedule.QueueKey,
            schedule.PayloadType,
            schedule.Payload,
            now,
            now,
            schedule.RetryStrategy,
            scheduleJobKey: schedule.JobKey,
            partitionKey: schedule.PartitionKey
        );

        await _storage.InsertAsync(job, cancellationToken);
        return DashboardCommandResult<JobActionResponse>.Ok(JobActionResponse.From(job));
    }

    private async Task<AtomizerSchedule?> GetScheduleAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        var schedules = await _storage.GetSchedulesAsync(cancellationToken);
        return schedules.FirstOrDefault(schedule => schedule.Id == scheduleId);
    }
}
