using System.Threading;
using System.Threading.Tasks;

namespace Atomizer.Abstractions
{
    public interface IAtomizerJobDispatcher
    {
        Task DispatchAsync(AtomizerJob job, CancellationToken cancellationToken);
    }
}
