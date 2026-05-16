using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.FlowTests.Drivers;

public abstract class FlowTestDriver : IAsyncLifetime
{
    public abstract string Name { get; }

    public virtual ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public virtual Task ResetAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public virtual void ConfigureServices(IServiceCollection services, string runId) { }

    public abstract void ConfigureStorage(AtomizerOptions options, string runId);
}
