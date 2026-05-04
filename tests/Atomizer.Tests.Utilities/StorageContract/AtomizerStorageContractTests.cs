using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Tests.Utilities.Stubs;
using Atomizer.Tests.Utilities.TestJobs;
using AwesomeAssertions;
using NSubstitute;

namespace Atomizer.Tests.Utilities.StorageContract;

/// <summary>
/// Abstract contract test base that verifies FIFO storage semantics (FIFO-07, FIFO-08, FIFO-09).
/// Subclass this in each storage backend test project and implement <see cref="CreateStorage"/>.
/// <para>
/// <strong>Pre-condition:</strong> The <see cref="IAtomizerStorage"/> returned by
/// <see cref="CreateStorage"/> must fully implement the FIFO partition-blocking rules
/// described in <see cref="IAtomizerStorage.GetDueJobsAsync"/>. Tests will fail if the
/// implementation does not enforce these rules.
/// </para>
/// </summary>
public abstract class AtomizerStorageContractTests : IAsyncLifetime
{
    private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();
    protected DateTimeOffset _now;
    protected IAtomizerStorage _sut = null!;

    /// <summary>
    /// Creates a fresh storage instance for the test run.
    /// </summary>
    /// <param name="clock">The clock instance the storage implementation must use.</param>
    /// <returns>A new <see cref="IAtomizerStorage"/> implementation to test.</returns>
    protected abstract IAtomizerStorage CreateStorage(IAtomizerClock clock);

    /// <inheritdoc />
    public ValueTask InitializeAsync()
    {
        _now = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(_now);
        _sut = CreateStorage(_clock);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ------------------------------------------------------------------
    // FIFO-09: SequenceNumber assignment on InsertAsync
    // ------------------------------------------------------------------

    /// <summary>
    /// FIFO-09: Partitioned jobs receive monotonically increasing sequence numbers.
    /// </summary>
    [Fact]
    public async Task InsertAsync_WhenPartitionedJob_ShouldAssignMonotonicallyIncreasingSequenceNumber()
    {
        // Arrange
        var partitionKey = new PartitionKey("order-123");
        var job1 = CreateJob(partitionKey: partitionKey);
        var job2 = CreateJob(partitionKey: partitionKey);

        // Act
        await _sut.InsertAsync(job1, CancellationToken.None);
        await _sut.InsertAsync(job2, CancellationToken.None);

        // Assert
        job1.SequenceNumber.Should().NotBeNull();
        job2.SequenceNumber.Should().NotBeNull();
        job2.SequenceNumber.Should().BeGreaterThan(job1.SequenceNumber!.Value);
    }

    /// <summary>
    /// FIFO-09: Unpartitioned jobs do not receive a sequence number.
    /// </summary>
    [Fact]
    public async Task InsertAsync_WhenUnpartitionedJob_ShouldNotAssignSequenceNumber()
    {
        // Arrange
        var job = CreateJob();

        // Act
        await _sut.InsertAsync(job, CancellationToken.None);

        // Assert
        job.SequenceNumber.Should().BeNull();
    }

    /// <summary>
    /// FIFO-09 / D-06: On an idempotency key collision, the existing job's sequence number
    /// is assigned to the passed-in job.
    /// </summary>
    [Fact]
    public async Task InsertAsync_WhenIdempotencyKeyCollision_ShouldAssignExistingSequenceNumber()
    {
        // Arrange
        var partitionKey = new PartitionKey("orders");
        const string idempotencyKey = "idem-key-1";

        var job1 = CreateJob(partitionKey: partitionKey, idempotencyKey: idempotencyKey);
        await _sut.InsertAsync(job1, CancellationToken.None);

        var job2 = CreateJob(partitionKey: partitionKey, idempotencyKey: idempotencyKey);

        // Act
        await _sut.InsertAsync(job2, CancellationToken.None);

        // Assert
        job1.SequenceNumber.Should().NotBeNull();
        job2.SequenceNumber.Should().Be(job1.SequenceNumber);
    }

    // ------------------------------------------------------------------
    // FIFO-07: GetDueJobsAsync returns all eligible jobs from unblocked partitions
    // ------------------------------------------------------------------

    /// <summary>
    /// FIFO-07: When multiple jobs share a partition key, all of them are returned in
    /// sequence-number order so a single sweep can process the whole partition.
    /// </summary>
    [Fact]
    public async Task GetDueJobsAsync_WhenMultipleJobsInSamePartition_ShouldReturnAllInSequenceOrder()
    {
        // Arrange
        var partitionKey = new PartitionKey("batch-key");
        var job1 = CreateJob(partitionKey: partitionKey);
        var job2 = CreateJob(partitionKey: partitionKey);

        await _sut.InsertAsync(job1, CancellationToken.None);
        await _sut.InsertAsync(job2, CancellationToken.None);

        // Act
        var result = await _sut.GetDueJobsAsync(QueueKey.Default, _now, batchSize: 10, CancellationToken.None);

        // Assert — both jobs are returned; job1 (lower sequence) comes first
        result.Should().HaveCount(2);
        result[0].Id.Should().Be(job1.Id);
        result[1].Id.Should().Be(job2.Id);
    }

    /// <summary>
    /// FIFO-07: The batch size cap applies across all eligible jobs regardless of partition.
    /// </summary>
    [Fact]
    public async Task GetDueJobsAsync_WhenBatchSizeSmallerThanPartitionJobs_ShouldRespectBatchSize()
    {
        // Arrange
        var partitionKey = new PartitionKey("big-partition");
        var jobs = Enumerable.Range(0, 5).Select(_ => CreateJob(partitionKey: partitionKey)).ToList();
        foreach (var job in jobs)
            await _sut.InsertAsync(job, CancellationToken.None);

        // Act
        var result = await _sut.GetDueJobsAsync(QueueKey.Default, _now, batchSize: 3, CancellationToken.None);

        // Assert — capped at batchSize, lowest sequence numbers first
        result.Should().HaveCount(3);
        result[0].Id.Should().Be(jobs[0].Id);
        result[1].Id.Should().Be(jobs[1].Id);
        result[2].Id.Should().Be(jobs[2].Id);
    }

    /// <summary>
    /// FIFO-07: Unpartitioned jobs are returned alongside the head of each partition.
    /// </summary>
    [Fact]
    public async Task GetDueJobsAsync_WhenUnpartitionedJobsExist_ShouldReturnThemAlongsidePartitionedJobs()
    {
        // Arrange
        var partitionedJob = CreateJob(partitionKey: new PartitionKey("p1"));
        var unpartitionedJob = CreateJob();

        await _sut.InsertAsync(partitionedJob, CancellationToken.None);
        await _sut.InsertAsync(unpartitionedJob, CancellationToken.None);

        // Act
        var result = await _sut.GetDueJobsAsync(QueueKey.Default, _now, batchSize: 10, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(j => j.Id == partitionedJob.Id);
        result.Should().Contain(j => j.Id == unpartitionedJob.Id);
    }

    // ------------------------------------------------------------------
    // FIFO-08: GetDueJobsAsync excludes entire partition when head is blocked
    // ------------------------------------------------------------------

    /// <summary>
    /// FIFO-08: When the head job of a partition is Processing, the entire partition is excluded.
    /// </summary>
    [Fact]
    public async Task GetDueJobsAsync_WhenPartitionIsBlockedByProcessing_ShouldExcludeEntirePartition()
    {
        // Arrange
        var partitionKey = new PartitionKey("blocked-p");
        var job1 = CreateJob(partitionKey: partitionKey);
        var job2 = CreateJob(partitionKey: partitionKey);

        await _sut.InsertAsync(job1, CancellationToken.None);
        await _sut.InsertAsync(job2, CancellationToken.None);

        // Transition job1 to Processing and persist
        job1.Lease(FakeDataFactory.LeaseToken(), _now, TimeSpan.FromMinutes(10));
        await _sut.UpdateJobsAsync([job1], CancellationToken.None);

        // Act
        var result = await _sut.GetDueJobsAsync(QueueKey.Default, _now, batchSize: 10, CancellationToken.None);

        // Assert — entire partition invisible while job1 is Processing
        result.Should().BeEmpty();
    }

    /// <summary>
    /// FIFO-08: When the head job of a partition is Pending with prior attempts (retrying),
    /// the entire partition is excluded.
    /// </summary>
    [Fact]
    public async Task GetDueJobsAsync_WhenPartitionIsBlockedByPendingWithAttempts_ShouldExcludeEntirePartition()
    {
        // Arrange
        var partitionKey = new PartitionKey("retry-p");
        var job1 = CreateJob(partitionKey: partitionKey);
        var job2 = CreateJob(partitionKey: partitionKey);

        await _sut.InsertAsync(job1, CancellationToken.None);
        await _sut.InsertAsync(job2, CancellationToken.None);

        // Simulate retry state: Lease → Attempt → Reschedule (Pending with Attempts = 1)
        job1.Lease(FakeDataFactory.LeaseToken(), _now, TimeSpan.FromMinutes(10));
        job1.Attempt();
        job1.Reschedule(_now, _now);
        await _sut.UpdateJobsAsync([job1], CancellationToken.None);

        // Act
        var result = await _sut.GetDueJobsAsync(QueueKey.Default, _now, batchSize: 10, CancellationToken.None);

        // Assert — partition blocked while job1 is Pending with Attempts > 0
        result.Should().BeEmpty();
    }

    /// <summary>
    /// FIFO-08: Two jobs sharing the same partition key string but in different queues
    /// are treated as independent partitions.
    /// </summary>
    [Fact]
    public async Task GetDueJobsAsync_WhenSamePartitionKeyInDifferentQueues_ShouldTreatAsIndependent()
    {
        var partitionKey = new PartitionKey("shared-key");
        var queueA = QueueKey.Default;
        var queueB = new QueueKey("secondary");

        var jobA = CreateJob(partitionKey: partitionKey, queueKey: queueA);
        var jobB = CreateJob(partitionKey: partitionKey, queueKey: queueB);

        await _sut.InsertAsync(jobA, CancellationToken.None);
        await _sut.InsertAsync(jobB, CancellationToken.None);

        jobA.Lease(FakeDataFactory.LeaseToken(), _now, TimeSpan.FromMinutes(10));
        await _sut.UpdateJobsAsync([jobA], CancellationToken.None);

        var result = await _sut.GetDueJobsAsync(queueB, _now, batchSize: 10, CancellationToken.None);

        result.Should().HaveCount(1);
        result.Single().Id.Should().Be(jobB.Id);
    }

    // ------------------------------------------------------------------
    // ReleaseLeasedAsync: partition unblocking
    // ------------------------------------------------------------------

    /// <summary>
    /// Releasing a leased partition head must clear VisibleAt and make the job visible again,
    /// unblocking the entire partition.
    /// </summary>
    [Fact]
    public async Task ReleaseLeasedAsync_WhenPartitionHeadReleased_ShouldUnblockPartition()
    {
        var partitionKey = new PartitionKey("release-p");
        var job1 = CreateJob(partitionKey: partitionKey);
        var job2 = CreateJob(partitionKey: partitionKey);

        await _sut.InsertAsync(job1, CancellationToken.None);
        await _sut.InsertAsync(job2, CancellationToken.None);

        var leaseToken = FakeDataFactory.LeaseToken();
        job1.Lease(leaseToken, _now, TimeSpan.FromMinutes(10));
        await _sut.UpdateJobsAsync([job1], CancellationToken.None);

        var blocked = await _sut.GetDueJobsAsync(QueueKey.Default, _now, batchSize: 10, CancellationToken.None);
        blocked.Should().BeEmpty();

        await _sut.ReleaseLeasedAsync(leaseToken, _now, CancellationToken.None);

        var unblocked = await _sut.GetDueJobsAsync(QueueKey.Default, _now, batchSize: 10, CancellationToken.None);
        unblocked.Should().HaveCount(2);
        unblocked[0].Id.Should().Be(job1.Id);
        unblocked[1].Id.Should().Be(job2.Id);
    }

    // ------------------------------------------------------------------
    // FIFO-13: terminal-state unblocking
    // ------------------------------------------------------------------

    /// <summary>
    /// FIFO-13: When the head job of a partition completes successfully, the partition
    /// is unblocked and the next job becomes eligible for processing.
    /// </summary>
    [Fact]
    public async Task GetDueJobsAsync_WhenPartitionHeadCompleted_ShouldUnblockNextJob()
    {
        // Arrange
        var partitionKey = new PartitionKey("complete-p");
        var job1 = CreateJob(partitionKey: partitionKey);
        var job2 = CreateJob(partitionKey: partitionKey);

        await _sut.InsertAsync(job1, CancellationToken.None);
        await _sut.InsertAsync(job2, CancellationToken.None);

        // Transition job1 to Completed: Lease → Attempt → MarkAsCompleted
        job1.Lease(FakeDataFactory.LeaseToken(), _now, TimeSpan.FromMinutes(10));
        job1.Attempt();
        job1.MarkAsCompleted(_now);
        await _sut.UpdateJobsAsync([job1], CancellationToken.None);

        // Act
        var result = await _sut.GetDueJobsAsync(QueueKey.Default, _now, batchSize: 10, CancellationToken.None);

        // Assert — job2 is now the partition head and must be returned
        result.Should().HaveCount(1);
        result.Single().Id.Should().Be(job2.Id);
    }

    /// <summary>
    /// FIFO-13: When the head job of a partition exhausts its retries and is marked Failed,
    /// the partition is unblocked and the next job becomes eligible for processing.
    /// </summary>
    [Fact]
    public async Task GetDueJobsAsync_WhenPartitionHeadFailed_ShouldUnblockNextJob()
    {
        // Arrange
        var partitionKey = new PartitionKey("failed-p");
        var job1 = CreateJob(partitionKey: partitionKey);
        var job2 = CreateJob(partitionKey: partitionKey);

        await _sut.InsertAsync(job1, CancellationToken.None);
        await _sut.InsertAsync(job2, CancellationToken.None);

        // Transition job1 to Failed: Lease → Attempt → MarkAsFailed
        job1.Lease(FakeDataFactory.LeaseToken(), _now, TimeSpan.FromMinutes(10));
        job1.Attempt();
        job1.MarkAsFailed(_now);
        await _sut.UpdateJobsAsync([job1], CancellationToken.None);

        // Act
        var result = await _sut.GetDueJobsAsync(QueueKey.Default, _now, batchSize: 10, CancellationToken.None);

        // Assert — job2 is now the partition head and must be returned
        result.Should().HaveCount(1);
        result.Single().Id.Should().Be(job2.Id);
    }

    // ------------------------------------------------------------------
    // Helper
    // ------------------------------------------------------------------

    private AtomizerJob CreateJob(
        PartitionKey? partitionKey = null,
        string? idempotencyKey = null,
        QueueKey? queueKey = null
    )
    {
        return AtomizerJob.Create(
            queueKey ?? QueueKey.Default,
            typeof(WriteLineJob),
            "{}",
            _now,
            _now,
            idempotencyKey: idempotencyKey,
            partitionKey: partitionKey
        );
    }
}
