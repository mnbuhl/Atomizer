using Atomizer.Abstractions;
using Atomizer.Dashboard.Services;

namespace Atomizer.Dashboard.Tests.Services;

/// <summary>
/// Unit tests for the <c>CancelJobAsync</c> method on <see cref="DashboardStorageAdapter"/>.
/// </summary>
public class DashboardStorageAdapterCancelTests
{
    private readonly IAtomizerDashboardStorage _inner = Substitute.For<IAtomizerDashboardStorage>();
    private readonly DashboardStorageAdapter _sut;
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    public DashboardStorageAdapterCancelTests()
    {
        _sut = new DashboardStorageAdapter(_inner);
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    /// <summary>
    /// A pending job should be marked as Cancelled and persisted via
    /// <see cref="IAtomizerStorage.UpdateJobsAsync"/>; the method should return
    /// <see langword="true"/>.
    /// </summary>
    [Fact]
    public async Task CancelJobAsync_WhenJobIsPending_ShouldMarkCancelledAndReturnTrue()
    {
        // Arrange
        var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "{}", _now, _now);

        _inner.GetJobByIdAsync(job.Id, CancellationToken.None).Returns(job);
        _inner
            .UpdateJobsAsync(Arg.Any<IEnumerable<AtomizerJob>>(), CancellationToken.None)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.CancelJobAsync(job.Id, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        job.Status.Should().Be(AtomizerJobStatus.Cancelled);

        await _inner
            .Received(1)
            .UpdateJobsAsync(
                Arg.Is<IEnumerable<AtomizerJob>>(jobs => jobs.Single().Id == job.Id),
                CancellationToken.None
            );
    }

    /// <summary>
    /// After a successful cancel the job's <see cref="AtomizerJob.LeaseToken"/> and
    /// <see cref="AtomizerJob.VisibleAt"/> should be cleared.
    /// </summary>
    [Fact]
    public async Task CancelJobAsync_WhenJobIsPending_ShouldClearLeaseTokenAndVisibleAt()
    {
        // Arrange
        var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "{}", _now, _now);

        _inner.GetJobByIdAsync(job.Id, CancellationToken.None).Returns(job);
        _inner
            .UpdateJobsAsync(Arg.Any<IEnumerable<AtomizerJob>>(), CancellationToken.None)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.CancelJobAsync(job.Id, CancellationToken.None);

        // Assert
        job.LeaseToken.Should().BeNull();
        job.VisibleAt.Should().BeNull();
    }

    // ── Guard: job not found ──────────────────────────────────────────────────

    /// <summary>
    /// When no job with the given ID exists the method should return
    /// <see langword="false"/> without calling <c>UpdateJobsAsync</c>.
    /// </summary>
    [Fact]
    public async Task CancelJobAsync_WhenJobNotFound_ShouldReturnFalseWithoutUpdate()
    {
        // Arrange
        var missingId = Guid.NewGuid();
        _inner.GetJobByIdAsync(missingId, CancellationToken.None).Returns((AtomizerJob?)null);

        // Act
        var result = await _sut.CancelJobAsync(missingId, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        await _inner
            .DidNotReceive()
            .UpdateJobsAsync(Arg.Any<IEnumerable<AtomizerJob>>(), Arg.Any<CancellationToken>());
    }

    // ── Guard: wrong status ───────────────────────────────────────────────────

    /// <summary>
    /// A job that is currently being processed must not be cancelled from the
    /// dashboard — the method should return <see langword="false"/>.
    /// </summary>
    [Fact]
    public async Task CancelJobAsync_WhenJobIsProcessing_ShouldReturnFalse()
    {
        // Arrange
        var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "{}", _now, _now);
        job.Lease(new LeaseToken("worker:*:default:*:abc"), _now, TimeSpan.FromMinutes(5));

        _inner.GetJobByIdAsync(job.Id, CancellationToken.None).Returns(job);

        // Act
        var result = await _sut.CancelJobAsync(job.Id, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        await _inner
            .DidNotReceive()
            .UpdateJobsAsync(Arg.Any<IEnumerable<AtomizerJob>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A completed job must not be re-cancelled; the method should return
    /// <see langword="false"/>.
    /// </summary>
    [Fact]
    public async Task CancelJobAsync_WhenJobIsCompleted_ShouldReturnFalse()
    {
        // Arrange
        var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "{}", _now, _now);
        job.Lease(new LeaseToken("worker:*:default:*:abc"), _now, TimeSpan.FromMinutes(5));
        job.Attempt();
        job.MarkAsCompleted(_now);

        _inner.GetJobByIdAsync(job.Id, CancellationToken.None).Returns(job);

        // Act
        var result = await _sut.CancelJobAsync(job.Id, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        await _inner
            .DidNotReceive()
            .UpdateJobsAsync(Arg.Any<IEnumerable<AtomizerJob>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A failed job must not be cancelled; the method should return
    /// <see langword="false"/> (use retry to re-enqueue it).
    /// </summary>
    [Fact]
    public async Task CancelJobAsync_WhenJobIsFailed_ShouldReturnFalse()
    {
        // Arrange
        var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "{}", _now, _now);
        job.Lease(new LeaseToken("worker:*:default:*:abc"), _now, TimeSpan.FromMinutes(5));
        job.Attempt();
        job.MarkAsFailed(_now);

        _inner.GetJobByIdAsync(job.Id, CancellationToken.None).Returns(job);

        // Act
        var result = await _sut.CancelJobAsync(job.Id, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        await _inner
            .DidNotReceive()
            .UpdateJobsAsync(Arg.Any<IEnumerable<AtomizerJob>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An already-cancelled job must not be cancelled again; the method should
    /// return <see langword="false"/>.
    /// </summary>
    [Fact]
    public async Task CancelJobAsync_WhenJobIsAlreadyCancelled_ShouldReturnFalse()
    {
        // Arrange
        var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "{}", _now, _now);
        job.Cancel(_now); // pre-cancel

        _inner.GetJobByIdAsync(job.Id, CancellationToken.None).Returns(job);

        // Act
        var result = await _sut.CancelJobAsync(job.Id, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        await _inner
            .DidNotReceive()
            .UpdateJobsAsync(Arg.Any<IEnumerable<AtomizerJob>>(), Arg.Any<CancellationToken>());
    }
}
