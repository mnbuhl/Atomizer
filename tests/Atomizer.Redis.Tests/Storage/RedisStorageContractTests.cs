using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Redis.Storage;
using Atomizer.Tests.Utilities.StorageContract;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Atomizer.Redis.Tests.Storage;

[Collection(nameof(RedisStorageFixture))]
public sealed class RedisStorageContractTests : AtomizerStorageContractTests
{
    private readonly RedisStorageFixture _fixture;

    public RedisStorageContractTests(RedisStorageFixture fixture)
    {
        _fixture = fixture;
    }

    protected override IAtomizerStorage CreateStorage(IAtomizerClock clock) =>
        new RedisStorage(
            _fixture.Connection,
            new RedisJobStorageOptions { KeyPrefix = $"contract:{Guid.NewGuid():N}" },
            clock,
            NullLogger<RedisStorage>.Instance
        );

    public override async ValueTask DisposeAsync()
    {
        await _fixture.FlushAsync();
    }
}
