using Atomizer.Abstractions;

namespace Atomizer.Storage;

public sealed class NoopLock : IAtomizerLock
{
    public void Dispose()
    {
        // No-op
    }

    public Task AcquireAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public bool Acquired => true;

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
    }
}
