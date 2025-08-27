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
            await _sut.UpdateJobAsync(job, CancellationToken.None);

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
            Func<Task> act = async () => await _sut.UpdateJobAsync(job, CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<KeyNotFoundException>();
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
    }
}
