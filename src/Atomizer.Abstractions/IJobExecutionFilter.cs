using System;
using System.Threading;
using System.Threading.Tasks;

namespace Atomizer.Abstractions
{
    public interface IJobExecutionFilter
    {
        Task OnExecutingAsync(AtomizerJob job, CancellationToken cancellationToken);
        Task OnSucceededAsync(AtomizerJob job, CancellationToken cancellationToken);
        Task OnFailedAsync(AtomizerJob job, Exception error, bool willRetry, CancellationToken cancellationToken);
    }
}
