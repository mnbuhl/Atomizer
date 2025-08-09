using System;
using System.Threading;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Hosting
{
    public class DefaultJobDispatcher : IJobDispatcher
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IJobTypeResolver _typeResolver;
        private readonly IJobSerializer _jobSerializer;

        public DefaultJobDispatcher(
            IServiceProvider serviceProvider,
            IJobTypeResolver typeResolver,
            IJobSerializer jobSerializer
        )
        {
            _serviceProvider = serviceProvider;
            _typeResolver = typeResolver;
            _jobSerializer = jobSerializer;
        }

        public Task DispatchAsync(AtomizerJob job, CancellationToken cancellationToken)
        {
            var handlerType = _typeResolver.Resolve(job.PayloadType);
            var payload = _jobSerializer.Deserialize(job.Payload, job.PayloadType);

            var handler = _serviceProvider.GetRequiredService(handlerType);
            var jobContext = new JobContext<>()
        }
    }
}
