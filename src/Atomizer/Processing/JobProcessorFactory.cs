using Atomizer.Abstractions;
using Atomizer.Hosting;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing
{
    internal interface IJobProcessorFactory
    {
        IJobProcessor Create(string processorId);
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

        public IJobProcessor Create(string processorId)
        {
            return new JobProcessor(
                _clock,
                _dispatcher,
                _storageScopeFactory,
                _loggerFactory.CreateLogger(processorId)
            );
        }
    }
}
