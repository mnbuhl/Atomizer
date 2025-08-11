using System.Threading;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Atomizer.Models;

// ReSharper disable once CheckNamespace
namespace Atomizer
{
    public interface IAtomizerJobHandler<in TPayload>
    {
        Task HandleAsync(TPayload payload, JobContext context);
    }

    public sealed class JobContext
    {
        /// <summary>
        /// The job that is being processed.
        /// </summary>
        public AtomizerJob Job { get; set; } = null!;

        /// <summary>
        /// Cancellation token to cancel the job processing.
        /// </summary>
        public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
    }
}
