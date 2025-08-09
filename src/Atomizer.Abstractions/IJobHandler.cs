using System.Threading;
using System.Threading.Tasks;

namespace Atomizer.Abstractions
{
    public interface IJobHandler<TPayload>
    {
        Task HandleAsync(JobContext<TPayload> context);
    }

    public sealed class JobContext<TPayload>
    {
        /// <summary>
        /// The payload of the job being processed.
        /// </summary>
        public TPayload Payload { get; set; } = default!;

        /// <summary>
        /// The job that is being processed.
        /// </summary>
        public AtomizerJob Job { get; set; } = null!;

        /// <summary>
        /// Cancellation token to cancel the job processing.
        /// </summary>
        public CancellationToken Cancellation { get; set; } = CancellationToken.None;
    }
}
