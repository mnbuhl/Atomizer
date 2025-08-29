using Atomizer.Abstractions;

namespace Atomizer.Locking;

public class NoopLeasingScopeFactory : IAtomizerLeasingScopeFactory
{
    public Task<IAtomizerLeasingScope> CreateScopeAsync(
        QueueKey key,
        TimeSpan scopeTimeout,
        CancellationToken cancellationToken
    )
    {
        IAtomizerLeasingScope noopLeasingScope = new NoopLeasingScope();
        return Task.FromResult(noopLeasingScope);
    }

    private sealed class NoopLeasingScope : IAtomizerLeasingScope
    {
        public bool Acquired => true;

        public void Dispose()
        {
            // No-op
        }

        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }
    }
}
