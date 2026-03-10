using Atomizer.Abstractions;
using Atomizer.Dashboard.Models;
using Atomizer.Dashboard.Services;

namespace Atomizer.Dashboard.Tests.Services;

/// <summary>
/// Unit tests for the job-mutation methods added to
/// <see cref="DashboardStorageAdapter"/>: <c>RetryJobAsync</c> and <c>CancelJobAsync</c>.
/// </summary>
public class DashboardStorageAdapterTests
{
    private readonly IAtomizerDashboardStorage _inner = Substitute.For<IAtomizerDashboardStorage>();
    private readonly DashboardStorageAdapter _sut;
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    public DashboardStorageAdapterTests()
    {
        _sut = new DashboardStorageAdapter(_inner);
    }

    // =========================================================================
    // RetryJobAsync
    // =========================================================================

    /// <summary>
    /// When the job does not exist in storage, <c>RetryJobAsync</c> should throw
    /// <see cref="InvalidOperationException"/> before attempting any insert.
    /// </summary>
    [Fact]
    public async Task RetryJobAsync_WhenJobNotFound_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        _inner.GetJobByIdAsync(jobId, CancellationToken.None).Returns((AtomizerJob?)null);

        // Act
        var act = () => _sut.RetryJobAsync(jobId, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        await _inner.DidNotReceive().InsertAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// When the job is in <see cref="AtomizerJobStatus.Pending"/> status,
    /// <c>RetryJobAsync</c> should throw because only <c>Failed</c> jobs can be retried.
    /// </summary>
    [Fact]
    public async Task RetryJobAsync_WhenJobIsPending_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "{}", _now, _now);
        _inner.GetJobByIdAsync(job.Id, CancellationToken.None).Returns(job);

        // Act
        var act = () => _sut.RetryJobAsync(job.Id, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        await _inner.DidNotReceive().InsertAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// When the job is in <see cref="AtomizerJobStatus.Completed"/> status,
    /// <c>RetryJobAsync</c> should throw because only <c>Failed</c> jobs can be retried.
    /// </summary>
    [Fact]
    public async Task RetryJobAsync_WhenJobIsCompleted_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "{}", _now, _now);
        job.Lease(new LeaseToken("worker:*:default:*:abc"), _now, TimeSpan.FromMinutes(5));
        job.Attempt();
        job.MarkAsCompleted(_now);

        _inner.GetJobByIdAsync(job.Id, CancellationToken.None).Returns(job);

        // Act
        var act = () => _sut.RetryJobAsync(job.Id, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// When the job has no <see cref="AtomizerJob.PayloadType"/>,
    /// <c>RetryJobAsync</c> should throw because the job cannot be reconstructed.
    /// </summary>
    [Fact]
    public async Task RetryJobAsync_WhenPayloadTypeIsNull_ShouldThrowInvalidOperationException()
    {
        // Arrange — build a Failed job then clear its PayloadType
        var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "{}", _now, _now);
        job.Lease(new LeaseToken("worker:*:default:*:abc"), _now, TimeSpan.FromMinutes(5));
        job.Attempt();
        job.MarkAsFailed(_now);
        job.PayloadType = null; // simulate missing type info

        _inner.GetJobByIdAsync(job.Id, CancellationToken.None).Returns(job);

        // Act
        var act = () => _sut.RetryJobAsync(job.Id, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// When the job is in <see cref="AtomizerJobStatus.Failed"/> status,
    /// <c>RetryJobAsync</c> should insert a new <see cref="AtomizerJobStatus.Pending"/>
    /// job and return the new job's identifier.
    /// </summary>
    [Fact]
    public async Task RetryJobAsync_WhenJobIsFailed_ShouldInsertNewPendingJobAndReturnNewId()
    {
        // Arrange
        var originalJob = BuildFailedJob();
        var expectedNewId = Guid.NewGuid();

        _inner.GetJobByIdAsync(originalJob.Id, CancellationToken.None).Returns(originalJob);
        _inner.InsertAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>()).Returns(expectedNewId);

        // Act
        var newId = await _sut.RetryJobAsync(originalJob.Id, CancellationToken.None);

        // Assert
        newId.Should().Be(expectedNewId);
        await _inner
            .Received(1)
            .InsertAsync(Arg.Is<AtomizerJob>(j => j.Status == AtomizerJobStatus.Pending), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The new job created by <c>RetryJobAsync</c> should inherit the original job's
    /// queue key, payload type, and payload body.
    /// </summary>
    [Fact]
    public async Task RetryJobAsync_WhenJobIsFailed_ShouldPreserveQueuePayloadAndPayloadType()
    {
        // Arrange
        var queue = new QueueKey("priority");
        var originalJob = AtomizerJob.Create(queue, typeof(int), """{"value":42}""", _now, _now);
        originalJob.Lease(new LeaseToken("worker:*:default:*:abc"), _now, TimeSpan.FromMinutes(5));
        originalJob.Attempt();
        originalJob.MarkAsFailed(_now);

        _inner.GetJobByIdAsync(originalJob.Id, CancellationToken.None).Returns(originalJob);
        _inner.InsertAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());

        // Act
        await _sut.RetryJobAsync(originalJob.Id, CancellationToken.None);

        // Assert
        await _inner
            .Received(1)
            .InsertAsync(
                Arg.Is<AtomizerJob>(j =>
                    j.QueueKey == queue && j.PayloadType == typeof(int) && j.Payload == """{"value":42}"""
                ),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>
    /// The new job should have its attempt counter reset to zero regardless of how many
    /// attempts the original failed job had.
    /// </summary>
    [Fact]
    public async Task RetryJobAsync_WhenJobIsFailed_ShouldResetAttemptCounterToZero()
    {
        // Arrange
        var originalJob = BuildFailedJob(attempts: 3);
        _inner.GetJobByIdAsync(originalJob.Id, CancellationToken.None).Returns(originalJob);
        _inner.InsertAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());

        // Act
        await _sut.RetryJobAsync(originalJob.Id, CancellationToken.None);

        // Assert
        await _inner.Received(1).InsertAsync(Arg.Is<AtomizerJob>(j => j.Attempts == 0), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The idempotency key must not be copied to the new job — every dashboard
    /// retry must always create a distinct new job.
    /// </summary>
    [Fact]
    public async Task RetryJobAsync_WhenJobIsFailed_ShouldClearIdempotencyKey()
    {
        // Arrange — failed job that had an idempotency key set
        var originalJob = AtomizerJob.Create(
            QueueKey.Default,
            typeof(string),
            "{}",
            _now,
            _now,
            idempotencyKey: "unique-key-123"
        );
        originalJob.Lease(new LeaseToken("worker:*:default:*:abc"), _now, TimeSpan.FromMinutes(5));
        originalJob.Attempt();
        originalJob.MarkAsFailed(_now);

        _inner.GetJobByIdAsync(originalJob.Id, CancellationToken.None).Returns(originalJob);
        _inner.InsertAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());

        // Act
        await _sut.RetryJobAsync(originalJob.Id, CancellationToken.None);

        // Assert
        await _inner
            .Received(1)
            .InsertAsync(Arg.Is<AtomizerJob>(j => j.IdempotencyKey == null), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// When the failed job was enqueued by a recurring schedule, the new retry job
    /// should preserve the <see cref="AtomizerJob.ScheduleJobKey"/> association.
    /// </summary>
    [Fact]
    public async Task RetryJobAsync_WhenJobIsFailed_ShouldPreserveScheduleJobKey()
    {
        // Arrange
        var scheduleKey = new JobKey("nightly-report");
        var originalJob = AtomizerJob.Create(
            QueueKey.Default,
            typeof(string),
            "{}",
            _now,
            _now,
            scheduleJobKey: scheduleKey
        );
        originalJob.Lease(new LeaseToken("worker:*:default:*:abc"), _now, TimeSpan.FromMinutes(5));
        originalJob.Attempt();
        originalJob.MarkAsFailed(_now);

        _inner.GetJobByIdAsync(originalJob.Id, CancellationToken.None).Returns(originalJob);
        _inner.InsertAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());

        // Act
        await _sut.RetryJobAsync(originalJob.Id, CancellationToken.None);

        // Assert
        await _inner
            .Received(1)
            .InsertAsync(
                Arg.Is<AtomizerJob>(j => j.ScheduleJobKey != null && j.ScheduleJobKey.Equals(scheduleKey)),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>
    /// The new retry job must receive a brand-new <see cref="Guid"/> identifier
    /// that is different from the original failed job's identifier.
    /// </summary>
    [Fact]
    public async Task RetryJobAsync_WhenJobIsFailed_ShouldAssignNewJobId()
    {
        // Arrange
        var originalJob = BuildFailedJob();
        _inner.GetJobByIdAsync(originalJob.Id, CancellationToken.None).Returns(originalJob);
        _inner.InsertAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());

        // Act
        await _sut.RetryJobAsync(originalJob.Id, CancellationToken.None);

        // Assert — the inserted job must have a different ID than the original
        await _inner
            .Received(1)
            .InsertAsync(Arg.Is<AtomizerJob>(j => j.Id != originalJob.Id), Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // CancelJobAsync
    // =========================================================================

    /// <summary>
    /// When the job does not exist in storage, <c>CancelJobAsync</c> should return
    /// <see langword="false"/> without calling <c>UpdateJobsAsync</c>.
    /// </summary>
    [Fact]
    public async Task CancelJobAsync_WhenJobNotFound_ShouldReturnFalse()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        _inner.GetJobByIdAsync(jobId, CancellationToken.None).Returns((AtomizerJob?)null);

        // Act
        var result = await _sut.CancelJobAsync(jobId, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        await _inner.DidNotReceive().UpdateJobsAsync(Arg.Any<IEnumerable<AtomizerJob>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// When the job is in <see cref="AtomizerJobStatus.Failed"/> status,
    /// <c>CancelJobAsync</c> should return <see langword="false"/> because only
    /// <see cref="AtomizerJobStatus.Pending"/> jobs can be cancelled.
    /// </summary>
    [Fact]
    public async Task CancelJobAsync_WhenJobIsFailed_ShouldReturnFalse()
    {
        // Arrange
        var job = BuildFailedJob();
        _inner.GetJobByIdAsync(job.Id, CancellationToken.None).Returns(job);

        // Act
        var result = await _sut.CancelJobAsync(job.Id, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        await _inner.DidNotReceive().UpdateJobsAsync(Arg.Any<IEnumerable<AtomizerJob>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// When the job is in <see cref="AtomizerJobStatus.Processing"/> status,
    /// <c>CancelJobAsync</c> should return <see langword="false"/>.
    /// </summary>
    [Fact]
    public async Task CancelJobAsync_WhenJobIsProcessing_ShouldReturnFalse()
    {
        // Arrange
        var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "{}", _now, _now);
        job.Lease(new LeaseToken("worker:*:default:*:xyz"), _now, TimeSpan.FromMinutes(5));

        _inner.GetJobByIdAsync(job.Id, CancellationToken.None).Returns(job);

        // Act
        var result = await _sut.CancelJobAsync(job.Id, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        await _inner.DidNotReceive().UpdateJobsAsync(Arg.Any<IEnumerable<AtomizerJob>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// When the job is in <see cref="AtomizerJobStatus.Pending"/> status,
    /// <c>CancelJobAsync</c> should cancel the job, persist it via
    /// <c>UpdateJobsAsync</c>, and return <see langword="true"/>.
    /// </summary>
    [Fact]
    public async Task CancelJobAsync_WhenJobIsPending_ShouldCancelJobAndReturnTrue()
    {
        // Arrange
        var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "{}", _now, _now);
        _inner.GetJobByIdAsync(job.Id, CancellationToken.None).Returns(job);
        _inner
            .UpdateJobsAsync(Arg.Any<IEnumerable<AtomizerJob>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.CancelJobAsync(job.Id, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        await _inner
            .Received(1)
            .UpdateJobsAsync(
                Arg.Is<IEnumerable<AtomizerJob>>(jobs =>
                    jobs.Any(j => j.Id == job.Id && j.Status == AtomizerJobStatus.Cancelled)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Builds a <see cref="AtomizerJob"/> in the <see cref="AtomizerJobStatus.Failed"/>
    /// state with the given number of recorded attempts.
    /// </summary>
    private AtomizerJob BuildFailedJob(int attempts = 1)
    {
        var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "{}", _now, _now);

        for (var i = 0; i < attempts; i++)
        {
            job.Lease(new LeaseToken($"worker:*:default:*:{Guid.NewGuid():N}"), _now, TimeSpan.FromMinutes(5));
            job.Attempt();

            if (i < attempts - 1)
            {
                // Reschedule intermediate attempts back to Pending so the lease call works
                job.Reschedule(_now.AddSeconds(15), _now);
            }
        }

        job.MarkAsFailed(_now);

        return job;
    }
}
