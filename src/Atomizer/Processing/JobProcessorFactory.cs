using Atomizer.Abstractions;
using Atomizer.Core;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing;

internal interface IJobProcessorFactory
{
    IJobProcessor Create(WorkerId workerId, Guid jobId);
}

internal sealed class JobProcessorFactory : IJobProcessorFactory
{
    private readonly IAtomizerClock _clock;
    private readonly IAtomizerJobDispatcher _dispatcher;
    private readonly IAtomizerStorageScopeFactory _storageScopeFactory;
    private readonly ILoggerFactory _loggerFactory;

    public JobProcessorFactory(
        IAtomizerClock clock,
        IAtomizerJobDispatcher dispatcher,
        IAtomizerStorageScopeFactory storageScopeFactory,
        ILoggerFactory loggerFactory
    )
    {
        _clock = clock;
        _dispatcher = dispatcher;
        _storageScopeFactory = storageScopeFactory;
        _loggerFactory = loggerFactory;
    }

    public IJobProcessor Create(WorkerId workerId, Guid jobId)
    {
        var processorId = $"{workerId}:*:{jobId}";

        return new JobProcessor(
            _clock,
            _dispatcher,
            _storageScopeFactory,
            _loggerFactory.CreateLogger($"Atomizer.Processing.JobProcessor;{processorId}")
        );
    }
}
