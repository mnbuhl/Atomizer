using System.Collections.Concurrent;
using Atomizer.Abstractions;
using Microsoft.Extensions.Logging;

namespace Atomizer.Core;

internal sealed class InMemoryLeasingScopeFactory : IAtomizerLeasingScopeFactory
{
    private static readonly ConcurrentDictionary<QueueKey, (SemaphoreSlim, DateTimeOffset)> Semaphores = new();

    private readonly IAtomizerClock _clock;
    private readonly ILogger<InMemoryLeasingScopeFactory> _logger;

    public InMemoryLeasingScopeFactory(IAtomizerClock clock, ILogger<InMemoryLeasingScopeFactory> logger)
    {
        _clock = clock;
        _logger = logger;
    }

    public Task<IAtomizerLeasingScope> CreateScopeAsync(
        QueueKey key,
        TimeSpan scopeTimeout,
        CancellationToken cancellationToken
    )
    {
        _logger.LogDebug("Acquiring in-memory leasing scope for queue {QueueKey}", key);
        var scope = InMemoryLeasingScope.AcquireAsync(key, scopeTimeout, _clock.UtcNow, cancellationToken);

        _logger.LogDebug(
            "In memory leasing scope for queue {QueueKey} acquired: {Acquired}",
            key,
            scope.Result.Acquired
        );

        return scope;
    }

    private sealed class InMemoryLeasingScope : IAtomizerLeasingScope
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _released;

        private InMemoryLeasingScope(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public bool Acquired { get; private set; }

        public static async Task<IAtomizerLeasingScope> AcquireAsync(
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

            return new InMemoryLeasingScope(semaphore) { Acquired = acquired };
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
