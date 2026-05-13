using Atomizer.Abstractions;
using Atomizer.Core;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing;

internal interface IJobWorkerFactory
{
    IJobWorker Create(QueueKey queueKey, int workerIndex);
}

internal sealed class JobWorkerFactory : IJobWorkerFactory
{
    private readonly IJobProcessorFactory _jobProcessorFactory;
    private readonly IAtomizerServiceScopeFactory _serviceScopeFactory;
    private readonly IAtomizerClock _clock;
    private readonly ILoggerFactory _loggerFactory;
    private readonly AtomizerRuntimeIdentity _identity;

    public JobWorkerFactory(
        ILoggerFactory loggerFactory,
        IJobProcessorFactory jobProcessorFactory,
        IAtomizerServiceScopeFactory serviceScopeFactory,
        IAtomizerClock clock,
        AtomizerRuntimeIdentity identity
    )
    {
        _loggerFactory = loggerFactory;
        _jobProcessorFactory = jobProcessorFactory;
        _serviceScopeFactory = serviceScopeFactory;
        _clock = clock;
        _identity = identity;
    }

    public IJobWorker Create(QueueKey queueKey, int workerIndex)
    {
        var workerId = new WorkerId(_identity.InstanceId, queueKey, workerIndex);
        return new JobWorker(
            workerId,
            _jobProcessorFactory,
            _serviceScopeFactory,
            _clock,
            _loggerFactory.CreateLogger($"{typeof(JobWorker).FullName};{workerId}")
        );
    }
}
