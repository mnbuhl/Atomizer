using Atomizer.Abstractions;
using Microsoft.Extensions.Logging;

namespace Atomizer.Core;

/// <summary>
/// Default implementation of <see cref="IAtomizerClient"/> that serializes payloads
/// and delegates to the configured <see cref="IAtomizerStorage"/>.
/// </summary>
public sealed class AtomizerClient : IAtomizerClient
{
    private readonly IAtomizerServiceScopeFactory _serviceScopeFactory;
    private readonly IAtomizerJobSerializer _jobSerializer;
    private readonly IAtomizerClock _clock;
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
    {
        _serviceScopeFactory = serviceScopeFactory;
        _jobSerializer = jobSerializer;
        _clock = clock;
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
            job.PayloadType!.FullName,
            job.QueueKey,
            job.ScheduledAt
        );

        return jobId;
    }
}
