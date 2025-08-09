using System.Threading;
using System.Threading.Tasks;

namespace Atomizer.Abstractions
{
    public interface IJobDispatcher
    {
        Task DispatchAsync(AtomizerJob job, CancellationToken cancellationToken);
    }
}
