using Atomizer.Core;
using Atomizer.Redis.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Atomizer.Redis.Tests.Storage;

[Collection(nameof(RedisStorageFixture))]
public sealed class RedisLeaseTests : IAsyncLifetime
{
    private readonly RedisStorageFixture _fixture;
    private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();
    private RedisStorage _sut = null!;

    public RedisLeaseTests(RedisStorageFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await _fixture.FlushAsync();
        _sut = new RedisStorage(
            _fixture.Connection,
            new RedisJobStorageOptions { KeyPrefix = $"lease:{Guid.NewGuid():N}" },
            _clock,
            NullLogger<RedisStorage>.Instance
        );
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.FlushAsync();
    }

    [Fact]
    public async Task ExecuteInLeaseAsync_WhenLeaseIsAlreadyHeld_ShouldNotInvokeSecondCallback()
    {
        var started = new TaskCompletionSource<object?>();
        var release = new TaskCompletionSource<object?>();
        var secondCallbackInvoked = false;

        var first = _sut.ExecuteInLeaseAsync(
            QueueKey.Default,
            async _ =>
            {
                started.SetResult(null);
                await release.Task;
                return 42;
            },
            CancellationToken.None
        );

        await started.Task;

        var second = await _sut.ExecuteInLeaseAsync(
            QueueKey.Default,
            _ =>
            {
                secondCallbackInvoked = true;
                return Task.FromResult(10);
            },
            CancellationToken.None
        );

        release.SetResult(null);
        var firstResult = await first;

        second.Should().Be(0);
        firstResult.Should().Be(42);
        secondCallbackInvoked.Should().BeFalse();
    }
}
