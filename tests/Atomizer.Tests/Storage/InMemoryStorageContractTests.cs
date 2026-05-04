using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Storage;
using Atomizer.Tests.Utilities.StorageContract;

namespace Atomizer.Tests.Storage;

/// <summary>
/// Concrete contract tests for <see cref="InMemoryStorage"/>.
/// Inherits the 8 FIFO contract tests from <see cref="AtomizerStorageContractTests"/>.
/// </summary>
public sealed class InMemoryStorageContractTests : AtomizerStorageContractTests
{
    /// <inheritdoc />
    protected override IAtomizerStorage CreateStorage(IAtomizerClock clock)
    {
        var options = new InMemoryJobStorageOptions { AmountOfJobsToRetainInMemory = 100 };
        var logger = Substitute.For<TestableLogger<InMemoryStorage>>();
        return new InMemoryStorage(options, clock, logger);
    }
}
