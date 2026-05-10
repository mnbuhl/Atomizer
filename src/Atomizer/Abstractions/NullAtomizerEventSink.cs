namespace Atomizer;

internal sealed class NullAtomizerEventSink : IAtomizerEventSink
{
    public Task OnJobStateChangedAsync(AtomizerJob job, CancellationToken cancellationToken) => Task.CompletedTask;
}
