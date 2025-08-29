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
    private readonly IAtomizerServiceScopeFactory _serviceScopeFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly AtomizerRuntimeIdentity _atomizerRuntimeIdentity;
    private readonly IAtomizerClock _clock;

    public QueuePumpFactory(
        IQueuePoller queuePoller,
        IAtomizerServiceScopeFactory serviceScopeFactory,
        ILoggerFactory loggerFactory,
        AtomizerRuntimeIdentity atomizerRuntimeIdentity,
        IJobWorkerFactory workerFactory,
        IAtomizerClock clock
    )
    {
        _queuePoller = queuePoller;
        _serviceScopeFactory = serviceScopeFactory;
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
            _serviceScopeFactory,
            _loggerFactory.CreateLogger<QueuePump>(),
            _workerFactory,
            _atomizerRuntimeIdentity,
            _clock
        );
    }
}
