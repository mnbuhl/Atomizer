using Atomizer.Abstractions;
using Atomizer.Core;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing;

internal interface IQueuePumpFactory
{
    IQueuePump Create(QueueOptions queue);
}

internal sealed class QueuePumpFactory : IQueuePumpFactory
{
    private readonly IQueuePoller _queuePoller;
    private readonly IJobWorkerFactory _workerFactory;
    private readonly IAtomizerStorageScopeFactory _storageScopeFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly AtomizerRuntimeIdentity _atomizerRuntimeIdentity;
    private readonly IAtomizerClock _clock;

    public QueuePumpFactory(
        IQueuePoller queuePoller,
        IAtomizerStorageScopeFactory storageScopeFactory,
        ILoggerFactory loggerFactory,
        AtomizerRuntimeIdentity atomizerRuntimeIdentity,
        IJobWorkerFactory workerFactory,
        IAtomizerClock clock
    )
    {
        _queuePoller = queuePoller;
        _storageScopeFactory = storageScopeFactory;
        _loggerFactory = loggerFactory;
        _atomizerRuntimeIdentity = atomizerRuntimeIdentity;
        _workerFactory = workerFactory;
        _clock = clock;
    }

    public IQueuePump Create(QueueOptions queue)
    {
        return new QueuePump(
            queue,
            _queuePoller,
            _storageScopeFactory,
            _loggerFactory.CreateLogger<QueuePump>(),
            _workerFactory,
            _atomizerRuntimeIdentity,
            _clock
        );
    }
}
