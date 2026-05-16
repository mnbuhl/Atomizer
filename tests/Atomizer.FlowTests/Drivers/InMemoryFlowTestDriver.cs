using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atomizer.FlowTests.Drivers;

public sealed class InMemoryFlowTestDriver : FlowTestDriver
{
    private InMemoryStorage? _storage;

    public override string Name => "InMemory";

    public override Task ResetAsync(CancellationToken cancellationToken)
    {
        _storage = null;
        return Task.CompletedTask;
    }

    public override void ConfigureStorage(AtomizerOptions options, string runId)
    {
        options.JobStorageOptions = new JobStorageOptions(sp =>
        {
            _storage ??= new InMemoryStorage(
                new InMemoryJobStorageOptions { AmountOfJobsToRetainInMemory = 1000 },
                sp.GetRequiredService<IAtomizerClock>(),
                NullLogger<InMemoryStorage>.Instance
            );

            return _storage;
        });
    }
}

[CollectionDefinition(nameof(InMemoryFlowTestDriver))]
public sealed class InMemoryFlowTestCollection : ICollectionFixture<InMemoryFlowTestDriver>;
