using System.Collections.Concurrent;
using Atomizer.Abstractions;
using Atomizer.Core;

namespace Atomizer.Locking;

internal sealed class InMemoryLockProvider : IAtomizerLockProvider
{
    private static readonly ConcurrentDictionary<QueueKey, (SemaphoreSlim, DateTimeOffset)> Semaphores = new();

    private readonly IAtomizerClock _clock;

    public InMemoryLockProvider(IAtomizerClock clock)
    {
        _clock = clock;
    }

    public Task<IAtomizerLock> AcquireLockAsync(QueueKey key, TimeSpan lockTimeout, CancellationToken cancellationToken)
    {
        return InMemoryLock.AcquireAsync(key, lockTimeout, _clock.UtcNow, cancellationToken);
    }

    private sealed class InMemoryLock : IAtomizerLock
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _released;

        private InMemoryLock(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public bool Acquired { get; private set; }
        public LockType Mode => LockType.InMemory;

        public static async Task<IAtomizerLock> AcquireAsync(
            QueueKey key,
            TimeSpan lockTimeout,
            DateTimeOffset acquiredAt,
            CancellationToken cancellationToken
        )
        {
            var (semaphore, acquiredTimestamp) = Semaphores.GetOrAdd(key, (new SemaphoreSlim(1, 1), acquiredAt));
            var acquired = await semaphore.WaitAsync(TimeSpan.Zero, cancellationToken);

            if (!acquired && (acquiredTimestamp + lockTimeout) < acquiredAt)
            {
                try
                {
                    // The lock has timed out, release it
                    semaphore.Release();
                }
                catch (SemaphoreFullException)
                {
                    // Ignore if the semaphore is already at max count
                }

                acquired = await semaphore.WaitAsync(TimeSpan.Zero, cancellationToken);
            }

            return new InMemoryLock(semaphore) { Acquired = acquired };
        }

        public void Dispose()
        {
            if (!_released && Acquired)
            {
                _released = true;
                try
                {
                    _semaphore.Release();
                }
                catch (SemaphoreFullException)
                {
                    // Ignore if the semaphore is already at max count
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            Dispose();
            await Task.CompletedTask;
        }
    }
}
