using System.Reflection;
using System.Runtime.ExceptionServices;
using Atomizer.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atomizer.Core;

public interface IAtomizerJobDispatcher
{
    Task DispatchAsync(AtomizerJob job, CancellationToken cancellationToken);
}

internal sealed class DefaultJobDispatcher : IAtomizerJobDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAtomizerJobTypeResolver _typeResolver;
    private readonly IAtomizerJobSerializer _jobSerializer;
    private readonly ILogger<DefaultJobDispatcher> _logger;

    public DefaultJobDispatcher(
        IServiceScopeFactory scopeFactory,
        IAtomizerJobTypeResolver typeResolver,
        IAtomizerJobSerializer jobSerializer,
        ILogger<DefaultJobDispatcher> logger
    )
    {
        _scopeFactory = scopeFactory;
        _typeResolver = typeResolver;
        _jobSerializer = jobSerializer;
        _logger = logger;
    }

    public async Task DispatchAsync(AtomizerJob job, CancellationToken cancellationToken)
    {
        if (job.PayloadType is null)
        {
            _logger.LogError("Job {JobId} has no payload type specified. Cannot dispatch job", job.Id);
            throw new InvalidOperationException($"Job {job.Id} has no payload type specified.");
        }

        var handlerType = _typeResolver.Resolve(job.PayloadType);
        var payload = _jobSerializer.Deserialize(job.Payload, job.PayloadType);

        if (payload is null)
        {
            _logger.LogError(
                "Job {JobId} payload deserialization failed for type {PayloadType}. Cannot dispatch job",
                job.Id,
                job.PayloadType.Name
            );
            throw new InvalidOperationException(
                $"Job {job.Id} payload deserialization failed for type {job.PayloadType.Name}."
            );
        }

        using var scope = _scopeFactory.CreateScope();

        var handler = scope.ServiceProvider.GetRequiredService(handlerType);

        var jobContext = new JobContext { Job = job, CancellationToken = cancellationToken };

        _logger.LogDebug(
            "Dispatching job {JobId} of type {JobType} with payload type {PayloadType}",
            job.Id,
            handlerType.Name,
            job.PayloadType.Name
        );

        try
        {
            var method = handlerType.GetMethod("HandleAsync", new[] { job.PayloadType, typeof(JobContext) })!;
            await (Task)method.Invoke(handler, new object[] { payload, jobContext })!;
        }
        catch (TargetInvocationException ex)
        {
            // Unwrap the inner exception to get the actual error
            _logger.LogError(
                ex.InnerException!,
                "Error while processing job {JobId} of type {JobType}",
                job.Id,
                handlerType.Name
            );
            ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); // Re-throw the inner exception for proper handling
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while processing job {JobId} of type {JobType}", job.Id, handlerType.Name);
            throw; // Re-throw to allow the job storage to handle retries or failures
        }

        _logger.LogDebug("Job {JobId} of type {JobType} completed successfully", job.Id, handlerType.Name);
    }
}
