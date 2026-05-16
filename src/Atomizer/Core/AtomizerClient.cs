using Atomizer.Abstractions;
using Microsoft.Extensions.Logging;

namespace Atomizer.Core;

/// <summary>
/// Default implementation of <see cref="IAtomizerClient"/> that serializes payloads
/// and delegates to the configured <see cref="IAtomizerStorage"/>.
/// </summary>
public sealed class AtomizerClient : IAtomizerClient
{
    private static readonly TimeSpan DirectExecutionHeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DirectExecutionVisibilityTimeout = TimeSpan.FromDays(1);

    private readonly IAtomizerServiceScopeFactory _serviceScopeFactory;
    private readonly IAtomizerJobSerializer _jobSerializer;
    private readonly IAtomizerClock _clock;
    private readonly IAtomizerJobDispatcher? _dispatcher;
    private readonly AtomizerRuntimeIdentity _identity;
    private readonly ILogger<AtomizerClient> _logger;

    /// <summary>
    /// Initializes a new <see cref="AtomizerClient"/> with the required dependencies.
    /// </summary>
    /// <param name="serviceScopeFactory">Factory used to create storage scopes.</param>
    /// <param name="jobSerializer">Serializer used to serialize job payloads.</param>
    /// <param name="clock">Clock abstraction for obtaining the current UTC time.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public AtomizerClient(
        IAtomizerServiceScopeFactory serviceScopeFactory,
        IAtomizerJobSerializer jobSerializer,
        IAtomizerClock clock,
        ILogger<AtomizerClient> logger
    )
        : this(serviceScopeFactory, jobSerializer, clock, null, new AtomizerRuntimeIdentity(), logger) { }

    /// <summary>
    /// Initializes a new <see cref="AtomizerClient"/> with the required dependencies.
    /// </summary>
    /// <param name="serviceScopeFactory">Factory used to create storage scopes.</param>
    /// <param name="jobSerializer">Serializer used to serialize job payloads.</param>
    /// <param name="clock">Clock abstraction for obtaining the current UTC time.</param>
    /// <param name="dispatcher">Dispatcher used to execute jobs directly.</param>
    /// <param name="identity">Runtime identity used to tag direct execution attempts.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public AtomizerClient(
        IAtomizerServiceScopeFactory serviceScopeFactory,
        IAtomizerJobSerializer jobSerializer,
        IAtomizerClock clock,
        IAtomizerJobDispatcher? dispatcher,
        AtomizerRuntimeIdentity identity,
        ILogger<AtomizerClient> logger
    )
    {
        _serviceScopeFactory = serviceScopeFactory;
        _jobSerializer = jobSerializer;
        _clock = clock;
        _dispatcher = dispatcher;
        _identity = identity;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<Guid> EnqueueAsync<TPayload>(
        TPayload payload,
        Action<EnqueueOptions>? configure = null,
        CancellationToken cancellation = default
    )
    {
        var options = new EnqueueOptions();
        configure?.Invoke(options);

        return EnqueueInternalAsync(payload, _clock.UtcNow, options, cancellation);
    }

    /// <inheritdoc/>
    public Task<Guid> ScheduleAsync<TPayload>(
        TPayload payload,
        DateTimeOffset runAt,
        Action<EnqueueOptions>? configure = null,
        CancellationToken cancellation = default
    )
    {
        var options = new EnqueueOptions();
        configure?.Invoke(options);

        return EnqueueInternalAsync(payload, runAt, options, cancellation);
    }

    /// <inheritdoc/>
    public async Task<Guid> ScheduleRecurringAsync<TPayload>(
        TPayload payload,
        JobKey name,
        Schedule schedule,
        Action<RecurringOptions>? configure = null,
        CancellationToken cancellation = default
    )
    {
        var options = new RecurringOptions();
        configure?.Invoke(options);

        var atomizerSchedule = AtomizerSchedule.Create(
            name,
            options.Queue,
            typeof(TPayload),
            _jobSerializer.Serialize(payload),
            schedule,
            options.TimeZone,
            _clock.UtcNow,
            options.MisfirePolicy,
            options.MaxCatchUp,
            options.Enabled,
            options.RetryStrategy,
            options.PartitionKey
        );

        using var scope = _serviceScopeFactory.CreateScope();
        return await scope.Storage.UpsertScheduleAsync(atomizerSchedule, cancellation);
    }

    /// <inheritdoc/>
    public async Task<bool> DequeueAsync(Guid jobId, CancellationToken cancellation = default)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var storage = scope.Storage;
        var job = await storage.GetJobByIdAsync(jobId, cancellation);
        if (job is null || job.Status != AtomizerJobStatus.Pending)
        {
            return false;
        }

        try
        {
            job.Cancel(_clock.UtcNow);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        await storage.UpdateJobsAsync(new[] { job }, cancellation);
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteRecurringAsync(JobKey name, CancellationToken cancellation = default)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        return await scope.Storage.DeleteScheduleAsync(name, cancellation);
    }

    /// <inheritdoc/>
    public async Task<Guid> ExecuteAsync<TPayload>(
        TPayload payload,
        Action<EnqueueOptions>? configure = null,
        CancellationToken cancellation = default
    )
    {
        if (_dispatcher is null)
        {
            throw new InvalidOperationException(
                "Direct job execution requires IAtomizerJobDispatcher. Use AddAtomizer to construct IAtomizerClient."
            );
        }

        var options = new EnqueueOptions();
        configure?.Invoke(options);

        var now = _clock.UtcNow;
        var job = AtomizerJob.Create(
            options.Queue,
            typeof(TPayload),
            _jobSerializer.Serialize(payload),
            now,
            now,
            options.RetryStrategy,
            options.IdempotencyKey,
            partitionKey: options.PartitionKey
        );

        job.Lease(CreateDirectLeaseToken(options.Queue), now, DirectExecutionVisibilityTimeout);

        using var scope = _serviceScopeFactory.CreateScope();
        var storage = scope.Storage;
        await storage.UpsertHeartbeatAsync(
            new AtomizerActiveServer { InstanceId = _identity.InstanceId, LastHeartbeatAt = now },
            cancellation
        );

        var jobId = await storage.InsertAsync(job, cancellation);
        if (jobId != job.Id)
        {
            _logger.LogDebug(
                "Direct execution skipped for existing idempotent job {JobId} with payload type {PayloadType}",
                jobId,
                job.PayloadType?.FullName
            );
            return jobId;
        }

        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var heartbeatTask = RunDirectExecutionHeartbeatAsync(heartbeatCts.Token);
        try
        {
            job.Attempt();
            await _dispatcher.DispatchAsync(job, cancellation);
            job.MarkAsCompleted(_clock.UtcNow);

            await storage.UpdateJobsAsync(new[] { job }, CancellationToken.None);

            _logger.LogInformation(
                "Direct execution of job {JobId} with payload type {PayloadType} completed",
                job.Id,
                job.PayloadType?.FullName
            );

            return job.Id;
        }
        catch (OperationCanceledException ex) when (cancellation.IsCancellationRequested)
        {
            job.Attempts -= 1;
            job.Release(_clock.UtcNow);

            await storage.UpdateJobsAsync(new[] { job }, CancellationToken.None);

            _logger.LogWarning(
                ex,
                "Direct execution of job {JobId} with payload type {PayloadType} was cancelled",
                job.Id,
                job.PayloadType?.FullName
            );

            throw;
        }
        catch (Exception ex)
        {
            var failedAt = _clock.UtcNow;
            job.Errors.Add(AtomizerJobError.Create(job.Id, failedAt, job.Attempts, ex, job.LeaseToken?.InstanceId));
            job.MarkAsFailed(failedAt);

            await storage.UpdateJobsAsync(new[] { job }, CancellationToken.None);

            _logger.LogError(
                ex,
                "Direct execution of job {JobId} with payload type {PayloadType} failed",
                job.Id,
                job.PayloadType?.FullName
            );

            throw;
        }
        finally
        {
            heartbeatCts.Cancel();
            await StopDirectExecutionHeartbeatAsync(heartbeatTask);
        }
    }

    private async Task<Guid> EnqueueInternalAsync<TPayload>(
        TPayload payload,
        DateTimeOffset when,
        EnqueueOptions options,
        CancellationToken ct
    )
    {
        var serializedPayload = _jobSerializer.Serialize(payload);

        var job = AtomizerJob.Create(
            options.Queue,
            typeof(TPayload),
            serializedPayload,
            _clock.UtcNow,
            when,
            options.RetryStrategy,
            options.IdempotencyKey,
            partitionKey: options.PartitionKey
        );

        using var scope = _serviceScopeFactory.CreateScope();
        var jobId = await scope.Storage.InsertAsync(job, ct);

        _logger.LogDebug(
            "Enqueuing job {JobId} with payload type {PayloadType} to queue {QueueKey} at {ScheduledAt}",
            jobId,
            job.PayloadType?.FullName,
            job.QueueKey,
            job.ScheduledAt
        );

        return jobId;
    }

    private LeaseToken CreateDirectLeaseToken(QueueKey queue) =>
        new LeaseToken($"{_identity.InstanceId}{LeaseToken.Delimiter}{queue}{LeaseToken.Delimiter}{Guid.NewGuid():N}");

    private async Task RunDirectExecutionHeartbeatAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(DirectExecutionHeartbeatInterval, cancellation);
                await UpsertDirectExecutionHeartbeatAsync(_clock.UtcNow, cancellation);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to refresh Atomizer heartbeat during direct execution for instance {InstanceId}",
                    _identity.InstanceId
                );
            }
        }
    }

    private async Task UpsertDirectExecutionHeartbeatAsync(DateTimeOffset now, CancellationToken cancellation)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        await scope.Storage.UpsertHeartbeatAsync(
            new AtomizerActiveServer { InstanceId = _identity.InstanceId, LastHeartbeatAt = now },
            cancellation
        );
    }

    private async Task StopDirectExecutionHeartbeatAsync(Task heartbeatTask)
    {
        try
        {
            await heartbeatTask;
        }
        catch (OperationCanceledException) { }
    }
}
