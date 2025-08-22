using Atomizer.Abstractions;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing
{
    internal interface IJobWorkerFactory
    {
        IJobWorker Create(string workerId);
    }

    internal sealed class JobWorkerFactory : IJobWorkerFactory
    {
        private readonly IJobProcessorFactory _jobProcessorFactory;
        private readonly IAtomizerStorageScopeFactory _storageScopeFactory;
        private readonly ILoggerFactory _loggerFactory;

        public JobWorkerFactory(
            IAtomizerStorageScopeFactory storageScopeFactory,
            ILoggerFactory loggerFactory,
            IJobProcessorFactory jobProcessorFactory
        )
        {
            _storageScopeFactory = storageScopeFactory;
            _loggerFactory = loggerFactory;
            _jobProcessorFactory = jobProcessorFactory;
        }

        public IJobWorker Create(string workerId)
        {
            return new JobWorker(
                workerId,
                _storageScopeFactory,
                _jobProcessorFactory,
                _loggerFactory.CreateLogger("Worker." + workerId)
            );
        }
    }
}
