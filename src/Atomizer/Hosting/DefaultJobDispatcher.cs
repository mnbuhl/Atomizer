using System.Threading;
using System.Threading.Tasks;
using Atomizer.Abstractions;

namespace Atomizer.Hosting
{
    internal class DefaultJobDispatcher : IJobDispatcher
    {
        private readonly IAtomizerServiceResolver _serviceResolver;
        private readonly IJobTypeResolver _typeResolver;
        private readonly IJobSerializer _jobSerializer;
        private readonly IAtomizerLogger<DefaultJobDispatcher> _logger;

        public DefaultJobDispatcher(
            IAtomizerServiceResolver serviceResolver,
            IJobTypeResolver typeResolver,
            IJobSerializer jobSerializer,
            IAtomizerLogger<DefaultJobDispatcher> logger
        )
        {
            _serviceResolver = serviceResolver;
            _typeResolver = typeResolver;
            _jobSerializer = jobSerializer;
            _logger = logger;
        }

        public async Task DispatchAsync(AtomizerJob job, CancellationToken cancellationToken)
        {
            var handlerType = _typeResolver.Resolve(job.PayloadType);
            var payload = _jobSerializer.Deserialize(job.Payload, job.PayloadType)!;

            using var scope = _serviceResolver.CreateScope();

            var handler = scope.Resolve(handlerType);
            var jobContext = new JobContext { Job = job, CancellationToken = cancellationToken };

            _logger.LogDebug(
                "Dispatching job {JobId} of type {JobType} with payload type {PayloadType}",
                job.Id,
                handlerType.Name,
                job.PayloadType.Name
            );

            var method = handlerType.GetMethod("HandleAsync", new[] { job.PayloadType, typeof(JobContext) })!;
            await (Task)method.Invoke(handler, new object[] { payload, jobContext })!;
        }
    }
}
