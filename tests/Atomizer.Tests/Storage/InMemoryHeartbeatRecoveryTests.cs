using Atomizer.Core;
using Atomizer.Storage;

namespace Atomizer.Tests.Storage;

public sealed class InMemoryHeartbeatRecoveryTests
{
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
    private readonly InMemoryStorage _storage;

    public InMemoryHeartbeatRecoveryTests()
    {
        var clock = Substitute.For<IAtomizerClock>();
        clock.UtcNow.Returns(_now);
        _storage = new InMemoryStorage(
            new InMemoryJobStorageOptions { AmountOfJobsToRetainInMemory = 100 },
            clock,
            Substitute.For<TestableLogger<InMemoryStorage>>()
        );
    }

    [Fact]
    public async Task HeartbeatUpsert_WhenRepeated_ShouldUpdateSingleActiveServerRecord()
    {
        var recovery = _storage;
        var first = _now.AddMinutes(-10);
        var second = _now;

        await recovery.UpsertHeartbeatAsync(
            new AtomizerActiveServer { InstanceId = "server-a", LastHeartbeatAt = first },
            CancellationToken.None
        );
        await recovery.UpsertHeartbeatAsync(
            new AtomizerActiveServer { InstanceId = "server-a", LastHeartbeatAt = second },
            CancellationToken.None
        );

        var stale = await recovery.GetStaleServersAsync(_now.AddMinutes(1), CancellationToken.None);

        stale.Should().ContainSingle(server => server.InstanceId == "server-a");
        stale.Single().LastHeartbeatAt.Should().Be(second);
    }

    [Fact]
    public async Task TryRecoverStaleServerAsync_WhenServerRevived_ShouldNoopAndReleaseNoJobs()
    {
        var recovery = _storage;
        await recovery.UpsertHeartbeatAsync(
            new AtomizerActiveServer { InstanceId = "server-a", LastHeartbeatAt = _now },
            CancellationToken.None
        );

        var result = await recovery.TryRecoverStaleServerAsync(
            "server-a",
            _now.AddMinutes(-1),
            _now,
            CancellationToken.None
        );

        result.Recovered.Should().BeFalse();
        result.ReleasedJobCount.Should().Be(0);
    }

    [Fact]
    public async Task TryRecoverStaleServerAsync_WhenStale_ShouldReleaseOnlyExactInstanceProcessingJobsAcrossQueues()
    {
        var recovery = _storage;
        var staleHeartbeat = _now.AddMinutes(-10);
        var staleBefore = _now.AddMinutes(-3);
        await recovery.UpsertHeartbeatAsync(
            new AtomizerActiveServer { InstanceId = "foo", LastHeartbeatAt = staleHeartbeat },
            CancellationToken.None
        );

        var exactDefault = await InsertLeasedJobAsync("foo:*:default:*:lease-1", QueueKey.Default);
        var exactOtherQueue = await InsertLeasedJobAsync("foo:*:other:*:lease-2", new QueueKey("other"));
        var prefixCollision = await InsertLeasedJobAsync("foobar:*:default:*:lease-3", QueueKey.Default);
        var wildcardInstance = await InsertLeasedJobAsync("f%o:*:default:*:lease-4", QueueKey.Default);

        var result = await recovery.TryRecoverStaleServerAsync("foo", staleBefore, _now, CancellationToken.None);

        result.Recovered.Should().BeTrue();
        result.ReleasedJobCount.Should().Be(2);
        exactDefault.Status.Should().Be(AtomizerJobStatus.Pending);
        exactDefault.LeaseToken.Should().BeNull();
        exactDefault.VisibleAt.Should().BeNull();
        exactOtherQueue.Status.Should().Be(AtomizerJobStatus.Pending);
        prefixCollision.Status.Should().Be(AtomizerJobStatus.Processing);
        wildcardInstance.Status.Should().Be(AtomizerJobStatus.Processing);
    }

    [Fact]
    public async Task TryRecoverStaleServerAsync_WhenTwoSweepersRace_ShouldHaveOneWinner()
    {
        var recovery = _storage;
        await recovery.UpsertHeartbeatAsync(
            new AtomizerActiveServer { InstanceId = "stale", LastHeartbeatAt = _now.AddMinutes(-10) },
            CancellationToken.None
        );
        await InsertLeasedJobAsync("stale:*:default:*:lease", QueueKey.Default);

        var attempts = Enumerable
            .Range(0, 2)
            .Select(_ =>
                recovery.TryRecoverStaleServerAsync("stale", _now.AddMinutes(-3), _now, CancellationToken.None)
            )
            .ToArray();

        var results = await Task.WhenAll(attempts);

        results.Count(result => result.Recovered).Should().Be(1);
        results.Sum(result => result.ReleasedJobCount).Should().Be(1);
    }

    private async Task<AtomizerJob> InsertLeasedJobAsync(string rawLeaseToken, QueueKey queueKey)
    {
        var job = AtomizerJob.Create(queueKey, typeof(string), "{}", _now, _now);
        await _storage.InsertAsync(job, CancellationToken.None);
        job.Lease(new LeaseToken(rawLeaseToken), _now, TimeSpan.FromMinutes(30));
        await _storage.UpdateJobsAsync([job], CancellationToken.None);
        return job;
    }
}
