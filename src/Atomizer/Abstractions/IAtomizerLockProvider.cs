namespace Atomizer.Abstractions;

public interface IAtomizerLockProvider
{
    Task<IAtomizerLock> AcquireLockAsync(QueueKey key, TimeSpan lockTimeout, CancellationToken cancellationToken);
}
