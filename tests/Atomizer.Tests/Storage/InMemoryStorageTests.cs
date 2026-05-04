using System.Collections.Concurrent;
using Atomizer;
using Atomizer.Core;
using Atomizer.Storage;

namespace Atomizer.Tests.Storage
{
    /// <summary>
    /// Unit tests for <see cref="InMemoryStorage"/>.
    /// </summary>
    public class InMemoryStorageTests
    {
        private readonly InMemoryJobStorageOptions _options = new InMemoryJobStorageOptions
        {
            AmountOfJobsToRetainInMemory = 100,
        };
        private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();
        private readonly TestableLogger<InMemoryStorage> _logger = Substitute.For<TestableLogger<InMemoryStorage>>();
        private readonly InMemoryStorage _sut;
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

        public InMemoryStorageTests()
        {
            _clock.UtcNow.Returns(_now);
            _sut = new InMemoryStorage(_options, _clock, _logger);
        }

        /// <summary>
        /// Verifies that InsertAsync stores the job and indexes it in the queue.
        /// </summary>
        [Fact]
        public async Task InsertAsync_WhenCalled_ShouldStoreJobAndIndexQueue()
        {
            // Arrange
            var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "payload", _now, _now);

            // Act
            var id = await _sut.InsertAsync(job, CancellationToken.None);

            // Assert
            id.Should().Be(job.Id);
            var jobs = NonPublicSpy.GetFieldValue<InMemoryStorage, ConcurrentDictionary<Guid, AtomizerJob>>(
                "_jobs",
                _sut
            );
            jobs.Should().ContainKey(job.Id);
            var queues = NonPublicSpy.GetFieldValue<
                InMemoryStorage,
                ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>>
            >("_queues", _sut);
            queues[QueueKey.Default].Should().ContainKey(job.Id);
        }

        [Fact]
        public async Task InsertAsync_WhenCalled_ShouldEvictOldJobs()
        {
            // Arrange
            var jobs = new List<AtomizerJob>();
            for (int i = 0; i < _options.AmountOfJobsToRetainInMemory + 10; i++)
            {
                var job = AtomizerJob.Create(
                    QueueKey.Default,
                    typeof(string),
                    $"payload-{i}",
                    _now.AddMinutes(i),
                    _now
                );
                job.Status = AtomizerJobStatus.Completed;
                jobs.Add(job);
                await _sut.InsertAsync(job, CancellationToken.None);
            }

            // Act
            var storedJobs = NonPublicSpy.GetFieldValue<InMemoryStorage, ConcurrentDictionary<Guid, AtomizerJob>>(
                "_jobs",
                _sut
            );

            // Assert
            storedJobs.Count.Should().Be(_options.AmountOfJobsToRetainInMemory);
            foreach (var job in jobs.Take(10))
            {
                storedJobs.ContainsKey(job.Id).Should().BeFalse();
            }
            foreach (var job in jobs.Skip(10))
            {
                storedJobs.ContainsKey(job.Id).Should().BeTrue();
            }
        }

        /// <summary>
        /// Verifies that UpdateAsync updates an existing job.
        /// </summary>
        [Fact]
        public async Task UpdateJobsAsync_WhenJobExists_ShouldUpdateJob()
        {
            // Arrange
            var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "payload", _now, _now);
            await _sut.InsertAsync(job, CancellationToken.None);
            job.Status = AtomizerJobStatus.Processing;

            // Act
            await _sut.UpdateJobsAsync([job], CancellationToken.None);

            // Assert
            var jobs = NonPublicSpy.GetFieldValue<InMemoryStorage, ConcurrentDictionary<Guid, AtomizerJob>>(
                "_jobs",
                _sut
            );
            jobs[job.Id].Status.Should().Be(AtomizerJobStatus.Processing);
        }

        /// <summary>
        /// Verifies that UpdateAsync throws when the job is missing.
        /// </summary>
        [Fact]
        public async Task UpdateAsync_WhenJobMissing_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "payload", _now, _now);

            // Act
            var act = async () => await _sut.UpdateJobsAsync([job], CancellationToken.None);

            // Assert
            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage($"Update requested for job {job.Id} that no longer exists in storage.");
        }

        /// <summary>
        /// Verifies that GetDueJobsAsync retrieves due jobs and updates their state.
        /// </summary>
        [Fact]
        public async Task GetDueJobsAsync_WhenJobsAvailable_ShouldGetJobs()
        {
            // Arrange
            var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "payload", _now, _now);
            await _sut.InsertAsync(job, CancellationToken.None);

            // Act
            var jobs = await _sut.GetDueJobsAsync(QueueKey.Default, _now, 1, CancellationToken.None);

            // Assert
            jobs.Should().ContainSingle();
            jobs[0].Id.Should().Be(job.Id);
        }

        /// <summary>
        /// Verifies that LeaseBatchAsync returns empty when the queue is empty.
        /// </summary>
        [Fact]
        public async Task LeaseBatchAsync_WhenQueueEmpty_ShouldReturnEmpty()
        {
            // Arrange

            // Act
            var leased = await _sut.GetDueJobsAsync(QueueKey.Default, _now, 1, CancellationToken.None);

            // Assert
            leased.Should().BeEmpty();
        }

        /// <summary>
        /// Verifies that ReleaseLeasedAsync releases leased jobs and resets their state.
        /// </summary>
        [Fact]
        public async Task ReleaseLeasedAsync_WhenJobsLeased_ShouldReleaseJobs()
        {
            // Arrange
            var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "payload", _now, _now);
            await _sut.InsertAsync(job, CancellationToken.None);
            var leaseToken = new LeaseToken("instance:*:default:*:lease1");
            job.Lease(leaseToken, _now, TimeSpan.FromMinutes(5));
            await _sut.UpdateJobsAsync(new[] { job }, CancellationToken.None);

            // Act
            var released = await _sut.ReleaseLeasedAsync(leaseToken, _clock.UtcNow, CancellationToken.None);

            // Assert
            released.Should().Be(1);
            var jobs = NonPublicSpy.GetFieldValue<InMemoryStorage, ConcurrentDictionary<Guid, AtomizerJob>>(
                "_jobs",
                _sut
            );
            jobs[job.Id].Status.Should().Be(AtomizerJobStatus.Pending);
            jobs[job.Id].LeaseToken.Should().BeNull();
            jobs[job.Id].VisibleAt.Should().BeNull();
            var leasesByToken = NonPublicSpy.GetFieldValue<
                InMemoryStorage,
                ConcurrentDictionary<string, ConcurrentDictionary<Guid, byte>>
            >("_leasesByToken", _sut);
            leasesByToken.ContainsKey(leaseToken.Token).Should().BeFalse();
        }

        /// <summary>
        /// Verifies that UpsertScheduleAsync stores the schedule.
        /// </summary>
        [Fact]
        public async Task UpsertScheduleAsync_WhenCalled_ShouldStoreSchedule()
        {
            // Arrange
            var schedule = AtomizerSchedule.Create(
                new JobKey("job1"),
                QueueKey.Default,
                typeof(string),
                "payload",
                Schedule.Default,
                TimeZoneInfo.Utc,
                _now
            );

            // Act
            var id = await _sut.UpsertScheduleAsync(schedule, CancellationToken.None);

            // Assert
            id.Should().Be(schedule.Id);
            var schedules = NonPublicSpy.GetFieldValue<InMemoryStorage, Dictionary<JobKey, AtomizerSchedule>>(
                "_schedules",
                _sut
            );
            schedules.Should().ContainKey(schedule.JobKey);
            schedules[schedule.JobKey].Should().Be(schedule);
        }

        /// <summary>
        /// Verifies that LeaseDueSchedulesAsync leases due schedules and updates their state.
        /// </summary>
        [Fact]
        public async Task GetDueSchedulesAsync_WhenDueSchedulesExist_ShouldGetSchedules()
        {
            // Arrange
            var schedule = AtomizerSchedule.Create(
                new JobKey("job1"),
                QueueKey.Default,
                typeof(string),
                "payload",
                Schedule.Default,
                TimeZoneInfo.Utc,
                _now.AddMinutes(-1)
            );
            await _sut.UpsertScheduleAsync(schedule, CancellationToken.None);

            // Act
            var leased = await _sut.GetDueSchedulesAsync(_now, CancellationToken.None);

            // Assert
            leased.Should().ContainSingle();
            leased[0].JobKey.Should().Be(schedule.JobKey);
            var schedules = NonPublicSpy.GetFieldValue<InMemoryStorage, Dictionary<JobKey, AtomizerSchedule>>(
                "_schedules",
                _sut
            );
            schedules[schedule.JobKey].JobKey.Should().Be(schedule.JobKey);
        }

        // ---- FIFO-09: InsertAsync sequence number assignment ----

        [Fact]
        public async Task InsertAsync_WhenPartitionedJob_ShouldAssignSequenceNumberStartingAtOne()
        {
            // Arrange
            var pk = new PartitionKey("order-1");
            var job1 = AtomizerJob.Create(QueueKey.Default, typeof(string), "p1", _now, _now, partitionKey: pk);
            var job2 = AtomizerJob.Create(QueueKey.Default, typeof(string), "p2", _now, _now, partitionKey: pk);

            // Act
            await _sut.InsertAsync(job1, CancellationToken.None);
            await _sut.InsertAsync(job2, CancellationToken.None);

            // Assert
            job1.SequenceNumber.Should().Be(1L);
            job2.SequenceNumber.Should().Be(2L);
        }

        [Fact]
        public async Task InsertAsync_WhenPartitionedJobsInDifferentQueues_ShouldAssignIndependentSequences()
        {
            // Arrange
            var pk = new PartitionKey("shared-key");
            var queueA = QueueKey.Default;
            var queueB = new QueueKey("queue-b");
            var jobA = AtomizerJob.Create(queueA, typeof(string), "pa", _now, _now, partitionKey: pk);
            var jobB = AtomizerJob.Create(queueB, typeof(string), "pb", _now, _now, partitionKey: pk);

            // Act
            await _sut.InsertAsync(jobA, CancellationToken.None);
            await _sut.InsertAsync(jobB, CancellationToken.None);

            // Assert — each queue starts its own sequence at 1
            jobA.SequenceNumber.Should().Be(1L);
            jobB.SequenceNumber.Should().Be(1L);
        }

        [Fact]
        public async Task InsertAsync_WhenUnpartitionedJob_ShouldLeaveSequenceNumberNull()
        {
            // Arrange
            var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "p", _now, _now);

            // Act
            await _sut.InsertAsync(job, CancellationToken.None);

            // Assert
            job.SequenceNumber.Should().BeNull();
        }

        [Fact]
        public async Task InsertAsync_WhenIdempotencyKeyCollision_ShouldReturnExistingIdAndAssignExistingSequenceNumber()
        {
            // Arrange
            var pk = new PartitionKey("idem-pk");
            const string idemKey = "test-idem-key";
            var job1 = AtomizerJob.Create(
                QueueKey.Default,
                typeof(string),
                "p1",
                _now,
                _now,
                idempotencyKey: idemKey,
                partitionKey: pk
            );
            await _sut.InsertAsync(job1, CancellationToken.None);

            var job2 = AtomizerJob.Create(
                QueueKey.Default,
                typeof(string),
                "p2",
                _now,
                _now,
                idempotencyKey: idemKey,
                partitionKey: pk
            );

            // Act
            var returnedId = await _sut.InsertAsync(job2, CancellationToken.None);

            // Assert
            returnedId.Should().Be(job1.Id);
            job2.SequenceNumber.Should().Be(job1.SequenceNumber);
        }

        [Fact]
        public async Task InsertAsync_WhenIdempotencyKeyCollision_ShouldNotIncreaseJobCount()
        {
            // Arrange
            const string idemKey = "idem-count-key";
            var pk = new PartitionKey("count-pk");
            var job1 = AtomizerJob.Create(
                QueueKey.Default,
                typeof(string),
                "p1",
                _now,
                _now,
                idempotencyKey: idemKey,
                partitionKey: pk
            );
            await _sut.InsertAsync(job1, CancellationToken.None);

            var job2 = AtomizerJob.Create(
                QueueKey.Default,
                typeof(string),
                "p2",
                _now,
                _now,
                idempotencyKey: idemKey,
                partitionKey: pk
            );

            // Act
            await _sut.InsertAsync(job2, CancellationToken.None);

            // Assert
            var jobs = NonPublicSpy.GetFieldValue<InMemoryStorage, ConcurrentDictionary<Guid, AtomizerJob>>(
                "_jobs",
                _sut
            );
            jobs.Count.Should().Be(1);
        }

        // ---- FIFO-07/FIFO-08: GetDueJobsAsync partition blocking ----

        [Fact]
        public async Task GetDueJobsAsync_WhenTwoJobsSharePartition_ShouldReturnBothInSequenceOrder()
        {
            // Arrange
            var pk = new PartitionKey("fifo-batch");
            var job1 = AtomizerJob.Create(QueueKey.Default, typeof(string), "p1", _now, _now, partitionKey: pk);
            var job2 = AtomizerJob.Create(QueueKey.Default, typeof(string), "p2", _now, _now, partitionKey: pk);
            await _sut.InsertAsync(job1, CancellationToken.None);
            await _sut.InsertAsync(job2, CancellationToken.None);

            // Act
            var result = await _sut.GetDueJobsAsync(QueueKey.Default, _now, 10, CancellationToken.None);

            // Assert — all partition jobs returned in sequence order
            result.Should().HaveCount(2);
            result[0].Id.Should().Be(job1.Id);
            result[1].Id.Should().Be(job2.Id);
        }

        [Fact]
        public async Task GetDueJobsAsync_WhenPartitionHeadAndUnpartitionedJobExist_ShouldReturnBoth()
        {
            // Arrange
            var pk = new PartitionKey("mixed-pk");
            var partitioned = AtomizerJob.Create(QueueKey.Default, typeof(string), "pp", _now, _now, partitionKey: pk);
            var unpartitioned = AtomizerJob.Create(QueueKey.Default, typeof(string), "up", _now, _now);
            await _sut.InsertAsync(partitioned, CancellationToken.None);
            await _sut.InsertAsync(unpartitioned, CancellationToken.None);

            // Act
            var result = await _sut.GetDueJobsAsync(QueueKey.Default, _now, 10, CancellationToken.None);

            // Assert — both returned
            result.Should().HaveCount(2);
            result.Should().Contain(j => j.Id == partitioned.Id);
            result.Should().Contain(j => j.Id == unpartitioned.Id);
        }

        [Fact]
        public async Task GetDueJobsAsync_WhenPartitionJobIsProcessing_ShouldReturnEmpty()
        {
            // Arrange
            var pk = new PartitionKey("blocked-pk");
            var job1 = AtomizerJob.Create(QueueKey.Default, typeof(string), "p1", _now, _now, partitionKey: pk);
            await _sut.InsertAsync(job1, CancellationToken.None);
            var leaseToken = new LeaseToken("inst:*:default:*:lease1");
            job1.Lease(leaseToken, _now, TimeSpan.FromMinutes(10));
            await _sut.UpdateJobsAsync([job1], CancellationToken.None);

            // Act
            var result = await _sut.GetDueJobsAsync(QueueKey.Default, _now, 10, CancellationToken.None);

            // Assert — partition blocked while job is Processing
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetDueJobsAsync_WhenPartitionJobIsPendingWithAttempts_ShouldReturnEmpty()
        {
            // Arrange
            var pk = new PartitionKey("retry-pk");
            var job1 = AtomizerJob.Create(QueueKey.Default, typeof(string), "p1", _now, _now, partitionKey: pk);
            await _sut.InsertAsync(job1, CancellationToken.None);
            // Simulate retry state: Lease → Attempt → Reschedule (Pending with Attempts = 1)
            var leaseToken = new LeaseToken("inst:*:default:*:lease2");
            job1.Lease(leaseToken, _now, TimeSpan.FromMinutes(10));
            job1.Attempt();
            job1.Reschedule(_now, _now);
            await _sut.UpdateJobsAsync([job1], CancellationToken.None);

            // Act
            var result = await _sut.GetDueJobsAsync(QueueKey.Default, _now, 10, CancellationToken.None);

            // Assert — partition blocked while job is Pending with Attempts > 0
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetDueJobsAsync_WhenQueueABlockedPartitionSameKeyAsQueueB_ShouldReturnQueueBJobUnaffected()
        {
            // Arrange
            var pk = new PartitionKey("cross-queue-pk");
            var queueB = new QueueKey("queue-b-test");
            var jobA = AtomizerJob.Create(QueueKey.Default, typeof(string), "pa", _now, _now, partitionKey: pk);
            var jobB = AtomizerJob.Create(queueB, typeof(string), "pb", _now, _now, partitionKey: pk);
            await _sut.InsertAsync(jobA, CancellationToken.None);
            await _sut.InsertAsync(jobB, CancellationToken.None);
            // Block partition in queue A
            var leaseToken = new LeaseToken("inst:*:default:*:lease3");
            jobA.Lease(leaseToken, _now, TimeSpan.FromMinutes(10));
            await _sut.UpdateJobsAsync([jobA], CancellationToken.None);

            // Act — query queue B
            var result = await _sut.GetDueJobsAsync(queueB, _now, 10, CancellationToken.None);

            // Assert — queue B is unaffected
            result.Should().HaveCount(1);
            result[0].Id.Should().Be(jobB.Id);
        }

        [Fact]
        public async Task GetDueJobsAsync_WhenProcessingJobHasExpiredVisibleAt_ShouldReturnIt()
        {
            // Arrange — Processing job with VisibleAt in the past (expired lease)
            var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "p", _now, _now);
            await _sut.InsertAsync(job, CancellationToken.None);
            var expiredNow = _now.AddMinutes(-10);
            var leaseToken = new LeaseToken("inst:*:default:*:lease4");
            job.Lease(leaseToken, expiredNow, TimeSpan.FromMinutes(1)); // VisibleAt = expiredNow + 1min = _now - 9min
            await _sut.UpdateJobsAsync([job], CancellationToken.None);

            // Act — query at _now (VisibleAt is in the past)
            var result = await _sut.GetDueJobsAsync(QueueKey.Default, _now, 10, CancellationToken.None);

            // Assert — expired lease job is still returned (existing behavior must not regress)
            result.Should().HaveCount(1);
            result[0].Id.Should().Be(job.Id);
        }
    }
}
