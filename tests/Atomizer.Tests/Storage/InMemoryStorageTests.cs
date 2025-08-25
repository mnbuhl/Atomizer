using System.Collections.Concurrent;
using Atomizer.Core;
using Atomizer.Storage;
using Microsoft.Extensions.Logging;

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
        private readonly ILogger<InMemoryStorage> _logger = Substitute.For<ILogger<InMemoryStorage>>();
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
            var queues = NonPublicSpy.GetFieldValue<InMemoryStorage, Dictionary<QueueKey, HashSet<Guid>>>(
                "_queues",
                _sut
            );
            queues[QueueKey.Default].Should().Contain(job.Id);
        }

        /// <summary>
        /// Verifies that UpdateAsync updates an existing job.
        /// </summary>
        [Fact]
        public async Task UpdateAsync_WhenJobExists_ShouldUpdateJob()
        {
            // Arrange
            var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "payload", _now, _now);
            await _sut.InsertAsync(job, CancellationToken.None);
            job.Status = AtomizerJobStatus.Processing;

            // Act
            await _sut.UpdateAsync(job, CancellationToken.None);

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
        public async Task UpdateAsync_WhenJobMissing_ShouldThrow()
        {
            // Arrange
            var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "payload", _now, _now);

            // Act
            Func<Task> act = async () => await _sut.UpdateAsync(job, CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<KeyNotFoundException>();
        }

        /// <summary>
        /// Verifies that LeaseBatchAsync leases jobs and updates their state.
        /// </summary>
        [Fact]
        public async Task LeaseBatchAsync_WhenJobsAvailable_ShouldLeaseJobs()
        {
            // Arrange
            var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "payload", _now, _now);
            await _sut.InsertAsync(job, CancellationToken.None);
            var leaseToken = new LeaseToken("instance:*:default:*:lease1");

            // Act
            var leased = await _sut.LeaseBatchAsync(
                QueueKey.Default,
                1,
                _now,
                TimeSpan.FromMinutes(1),
                leaseToken,
                CancellationToken.None
            );

            // Assert
            leased.Should().ContainSingle();
            leased[0].Status.Should().Be(AtomizerJobStatus.Processing);
            leased[0].LeaseToken.Should().Be(leaseToken);
            var leasesByToken = NonPublicSpy.GetFieldValue<
                InMemoryStorage,
                ConcurrentDictionary<string, ConcurrentDictionary<Guid, byte>>
            >("_leasesByToken", _sut);
            leasesByToken.Should().ContainKey(leaseToken.Token);
            leasesByToken[leaseToken.Token].Should().ContainKey(job.Id);
        }

        /// <summary>
        /// Verifies that LeaseBatchAsync returns empty when the queue is empty.
        /// </summary>
        [Fact]
        public async Task LeaseBatchAsync_WhenQueueEmpty_ShouldReturnEmpty()
        {
            // Arrange
            var leaseToken = new LeaseToken("instance:*:default:*:lease1");

            // Act
            var leased = await _sut.LeaseBatchAsync(
                QueueKey.Default,
                1,
                _now,
                TimeSpan.FromMinutes(1),
                leaseToken,
                CancellationToken.None
            );

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
            await _sut.LeaseBatchAsync(
                QueueKey.Default,
                1,
                _now,
                TimeSpan.FromMinutes(1),
                leaseToken,
                CancellationToken.None
            );

            // Act
            var released = await _sut.ReleaseLeasedAsync(leaseToken, CancellationToken.None);

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
        public async Task LeaseDueSchedulesAsync_WhenDueSchedulesExist_ShouldLeaseSchedules()
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
            var leaseToken = new LeaseToken("instance:*:default:*:lease1");

            // Act
            var leased = await _sut.LeaseDueSchedulesAsync(
                _now,
                TimeSpan.FromMinutes(1),
                leaseToken,
                CancellationToken.None
            );

            // Assert
            leased.Should().ContainSingle();
            leased[0].LeaseToken.Should().Be(leaseToken);
            var schedules = NonPublicSpy.GetFieldValue<InMemoryStorage, Dictionary<JobKey, AtomizerSchedule>>(
                "_schedules",
                _sut
            );
            schedules[schedule.JobKey].LeaseToken.Should().Be(leaseToken);
        }

        /// <summary>
        /// Verifies that ReleaseLeasedSchedulesAsync releases leased schedules and resets their state.
        /// </summary>
        [Fact]
        public async Task ReleaseLeasedSchedulesAsync_WhenSchedulesLeased_ShouldReleaseSchedules()
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
            var leaseToken = new LeaseToken("instance:*:default:*:lease1");
            await _sut.LeaseDueSchedulesAsync(_now, TimeSpan.FromMinutes(1), leaseToken, CancellationToken.None);

            // Act
            var released = await _sut.ReleaseLeasedSchedulesAsync(leaseToken, CancellationToken.None);

            // Assert
            released.Should().Be(1);
            var schedules = NonPublicSpy.GetFieldValue<InMemoryStorage, Dictionary<JobKey, AtomizerSchedule>>(
                "_schedules",
                _sut
            );
            schedules[schedule.JobKey].LeaseToken.Should().BeNull();
            schedules[schedule.JobKey].VisibleAt.Should().BeNull();
        }
    }
}
