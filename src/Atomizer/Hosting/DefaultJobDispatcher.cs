using System.Threading;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atomizer.Hosting
{
    internal class DefaultJobDispatcher : IJobDispatcher
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IJobTypeResolver _typeResolver;
        private readonly IJobSerializer _jobSerializer;
        private readonly ILogger<DefaultJobDispatcher> _logger;

        public DefaultJobDispatcher(
            IServiceScopeFactory scopeFactory,
            IJobTypeResolver typeResolver,
            IJobSerializer jobSerializer,
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
            var handlerType = _typeResolver.Resolve(job.PayloadType);
            var payload = _jobSerializer.Deserialize(job.Payload, job.PayloadType)!;

            using var scope = _scopeFactory.CreateScope();

            var handler = scope.ServiceProvider.GetRequiredService(handlerType);
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
