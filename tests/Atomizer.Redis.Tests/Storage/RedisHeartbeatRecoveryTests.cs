using Atomizer.Core;
using Atomizer.Redis.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Atomizer.Redis.Tests.Storage;

[Collection(nameof(RedisStorageFixture))]
public sealed class RedisHeartbeatRecoveryTests : IAsyncLifetime
{
    private readonly RedisStorageFixture _fixture;
    private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();
    private readonly DateTimeOffset _now = new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private RedisStorage _sut = null!;

    public RedisHeartbeatRecoveryTests(RedisStorageFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await _fixture.FlushAsync();
        _clock.UtcNow.Returns(_now);
        _sut = new RedisStorage(
            _fixture.Connection,
            new RedisJobStorageOptions { KeyPrefix = $"heartbeat:{Guid.NewGuid():N}" },
            _clock,
            NullLogger<RedisStorage>.Instance
        );
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.FlushAsync();
    }

    [Fact]
    public async Task TryRecoverStaleServerAsync_WhenServerIsStale_ShouldRemoveHeartbeatAndReleaseProcessingJobs()
    {
        var leaseToken = new LeaseToken($"server-a:*:{QueueKey.Default.Key}:*:{Guid.NewGuid()}");
        var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "{}", _now, _now);
        await _sut.InsertAsync(job, CancellationToken.None);
        job.Lease(leaseToken, _now, TimeSpan.FromMinutes(5));
        await _sut.UpdateJobsAsync(new[] { job }, CancellationToken.None);
        await _sut.UpsertHeartbeatAsync(
            new AtomizerActiveServer { InstanceId = "server-a", LastHeartbeatAt = _now.AddMinutes(-10) },
            CancellationToken.None
        );

        var result = await _sut.TryRecoverStaleServerAsync(
            "server-a",
            _now.AddMinutes(-5),
            _now.AddMinutes(1),
            CancellationToken.None
        );

        result.Recovered.Should().BeTrue();
        result.ReleasedJobCount.Should().Be(1);

        var stored = await _sut.GetJobByIdAsync(job.Id, CancellationToken.None);
        stored!.Status.Should().Be(AtomizerJobStatus.Pending);
        stored.LeaseToken.Should().BeNull();

        var servers = await _sut.GetActiveServersAsync(CancellationToken.None);
        servers.Should().NotContain(server => server.InstanceId == "server-a");
    }
}
