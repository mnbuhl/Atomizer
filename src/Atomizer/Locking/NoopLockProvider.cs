using Atomizer.Abstractions;

namespace Atomizer.Locking;

public class NoopLockProvider : IAtomizerLockProvider
{
    public Task<IAtomizerLock> AcquireLockAsync(QueueKey key, TimeSpan lockTimeout, CancellationToken cancellationToken)
    {
        IAtomizerLock noopLock = new NoopLock();
        return Task.FromResult(noopLock);
    }

    private sealed class NoopLock : IAtomizerLock
    {
        public bool Acquired => true;
        public LockType Mode => LockType.InMemory;

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
