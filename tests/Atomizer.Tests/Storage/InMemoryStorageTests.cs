using System.Collections.Concurrent;
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
            var queues = NonPublicSpy.GetFieldValue<InMemoryStorage, Dictionary<QueueKey, HashSet<Guid>>>(
                "_queues",
                _sut
            );
            queues[QueueKey.Default].Should().Contain(job.Id);
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
        public async Task UpdateAsync_WhenJobMissing_ShouldLogError()
        {
            // Arrange
            var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "payload", _now, _now);

            // Act
            await _sut.UpdateJobsAsync([job], CancellationToken.None);

            // Assert
            _logger.Received(1).LogError($"Update requested for missing job {job.Id}");
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

        /// <summary>
        /// Verifies that GetQueueStatsAsync returns correct counts grouped by queue key.
        /// </summary>
        [Fact]
        public async Task GetQueueStatsAsync_WhenJobsExistAcrossQueues_ShouldReturnCorrectStatsByQueue()
        {
            // Arrange
            var queueA = new QueueKey("queue-a");
            var queueB = new QueueKey("queue-b");

            // queueA: 2 pending, 1 processing, 1 completed, 1 failed
            var pendingA1 = AtomizerJob.Create(queueA, typeof(string), "{}", _now, _now);
            var pendingA2 = AtomizerJob.Create(queueA, typeof(string), "{}", _now, _now);
            var processingA = AtomizerJob.Create(queueA, typeof(string), "{}", _now, _now);
            processingA.Lease(new LeaseToken("w:*:queue-a:*:1"), _now, TimeSpan.FromMinutes(5));
            var completedA = AtomizerJob.Create(queueA, typeof(string), "{}", _now, _now);
            completedA.Lease(new LeaseToken("w:*:queue-a:*:2"), _now, TimeSpan.FromMinutes(5));
            completedA.Attempt();
            completedA.MarkAsCompleted(_now);
            var failedA = AtomizerJob.Create(queueA, typeof(string), "{}", _now, _now);
            failedA.Lease(new LeaseToken("w:*:queue-a:*:3"), _now, TimeSpan.FromMinutes(5));
            failedA.Attempt();
            failedA.MarkAsFailed(_now);

            // queueB: 1 pending
            var pendingB = AtomizerJob.Create(queueB, typeof(string), "{}", _now, _now);

            foreach (var job in new[] { pendingA1, pendingA2, processingA, completedA, failedA, pendingB })
                await _sut.InsertAsync(job, CancellationToken.None);

            await _sut.UpdateJobsAsync(new[] { processingA, completedA, failedA }, CancellationToken.None);

            // Act
            var stats = await _sut.GetQueueStatsAsync(CancellationToken.None);

            // Assert
            stats.Should().HaveCount(2);

            var statsA = stats.Single(s => s.QueueKey == queueA);
            statsA.Pending.Should().Be(2);
            statsA.Processing.Should().Be(1);
            statsA.Completed.Should().Be(1);
            statsA.Failed.Should().Be(1);
            statsA.Total.Should().Be(5);

            var statsB = stats.Single(s => s.QueueKey == queueB);
            statsB.Pending.Should().Be(1);
            statsB.Processing.Should().Be(0);
            statsB.Completed.Should().Be(0);
            statsB.Failed.Should().Be(0);
        }

        /// <summary>
        /// Verifies that GetQueueStatsAsync returns an empty list when no jobs exist.
        /// </summary>
        [Fact]
        public async Task GetQueueStatsAsync_WhenNoJobsExist_ShouldReturnEmptyList()
        {
            // Act
            var stats = await _sut.GetQueueStatsAsync(CancellationToken.None);

            // Assert
            stats.Should().BeEmpty();
        }

        /// <summary>
        /// Verifies that GetQueueStatsAsync returns results ordered alphabetically by queue name.
        /// </summary>
        [Fact]
        public async Task GetQueueStatsAsync_WhenMultipleQueues_ShouldReturnOrderedAlphabetically()
        {
            // Arrange — insert in reverse alphabetical order
            var queueZ = new QueueKey("z-queue");
            var queueA = new QueueKey("a-queue");
            var queueM = new QueueKey("m-queue");

            await _sut.InsertAsync(
                AtomizerJob.Create(queueZ, typeof(string), "{}", _now, _now),
                CancellationToken.None
            );
            await _sut.InsertAsync(
                AtomizerJob.Create(queueA, typeof(string), "{}", _now, _now),
                CancellationToken.None
            );
            await _sut.InsertAsync(
                AtomizerJob.Create(queueM, typeof(string), "{}", _now, _now),
                CancellationToken.None
            );

            // Act
            var stats = await _sut.GetQueueStatsAsync(CancellationToken.None);

            // Assert
            stats.Should().HaveCount(3);
            stats[0].QueueKey.Key.Should().Be("a-queue");
            stats[1].QueueKey.Key.Should().Be("m-queue");
            stats[2].QueueKey.Key.Should().Be("z-queue");
        }

        /// <summary>
        /// Verifies that GetRecentJobsAsync returns jobs ordered by creation time descending.
        /// </summary>
        [Fact]
        public async Task GetRecentJobsAsync_WhenJobsExist_ShouldReturnMostRecentFirst()
        {
            // Arrange
            var oldest = AtomizerJob.Create(
                QueueKey.Default,
                typeof(string),
                "{}",
                _now.AddMinutes(-10),
                _now.AddMinutes(-10)
            );
            var middle = AtomizerJob.Create(
                QueueKey.Default,
                typeof(string),
                "{}",
                _now.AddMinutes(-5),
                _now.AddMinutes(-5)
            );
            var newest = AtomizerJob.Create(QueueKey.Default, typeof(string), "{}", _now, _now);

            await _sut.InsertAsync(oldest, CancellationToken.None);
            await _sut.InsertAsync(middle, CancellationToken.None);
            await _sut.InsertAsync(newest, CancellationToken.None);

            // Act
            var jobs = await _sut.GetRecentJobsAsync(0, 10, CancellationToken.None);

            // Assert
            jobs.Should().HaveCount(3);
            jobs[0].Id.Should().Be(newest.Id);
            jobs[1].Id.Should().Be(middle.Id);
            jobs[2].Id.Should().Be(oldest.Id);
        }

        /// <summary>
        /// Verifies that GetRecentJobsAsync respects the skip and take parameters for pagination.
        /// </summary>
        [Fact]
        public async Task GetRecentJobsAsync_WhenPaginationApplied_ShouldReturnCorrectPage()
        {
            // Arrange — insert 5 jobs with distinct creation times
            var jobs = new List<AtomizerJob>();
            for (var i = 0; i < 5; i++)
            {
                var job = AtomizerJob.Create(
                    QueueKey.Default,
                    typeof(string),
                    "{}",
                    _now.AddMinutes(i),
                    _now.AddMinutes(i)
                );
                jobs.Add(job);
                await _sut.InsertAsync(job, CancellationToken.None);
            }

            // Act — skip first 2 (newest), take next 2
            var page = await _sut.GetRecentJobsAsync(2, 2, CancellationToken.None);

            // Assert — ordered descending by CreatedAt, so index 2 = third newest
            page.Should().HaveCount(2);
            page[0].Id.Should().Be(jobs[2].Id); // third newest
            page[1].Id.Should().Be(jobs[1].Id); // fourth newest
        }

        /// <summary>
        /// Verifies that GetRecentJobsAsync returns an empty list when no jobs exist.
        /// </summary>
        [Fact]
        public async Task GetRecentJobsAsync_WhenNoJobsExist_ShouldReturnEmptyList()
        {
            // Act
            var jobs = await _sut.GetRecentJobsAsync(0, 10, CancellationToken.None);

            // Assert
            jobs.Should().BeEmpty();
        }

        /// <summary>
        /// Verifies that GetJobByIdAsync returns the matching job when it exists.
        /// </summary>
        [Fact]
        public async Task GetJobByIdAsync_WhenJobExists_ShouldReturnJob()
        {
            // Arrange
            var job = AtomizerJob.Create(QueueKey.Default, typeof(string), """{"x":1}""", _now, _now);
            await _sut.InsertAsync(job, CancellationToken.None);

            // Act
            var result = await _sut.GetJobByIdAsync(job.Id, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result!.Id.Should().Be(job.Id);
            result.QueueKey.Should().Be(QueueKey.Default);
            result.Payload.Should().Be("""{"x":1}""");
        }

        /// <summary>
        /// Verifies that GetJobByIdAsync returns null when no job with the given id exists.
        /// </summary>
        [Fact]
        public async Task GetJobByIdAsync_WhenJobDoesNotExist_ShouldReturnNull()
        {
            // Arrange
            var unknownId = Guid.NewGuid();

            // Act
            var result = await _sut.GetJobByIdAsync(unknownId, CancellationToken.None);

            // Assert
            result.Should().BeNull();
        }

        /// <summary>
        /// Verifies that GetJobByIdAsync does not return a job whose id differs from the query.
        /// </summary>
        [Fact]
        public async Task GetJobByIdAsync_WhenOtherJobsExist_ShouldReturnOnlyMatchingJob()
        {
            // Arrange
            var job1 = AtomizerJob.Create(QueueKey.Default, typeof(string), "{}", _now, _now);
            var job2 = AtomizerJob.Create(QueueKey.Default, typeof(string), "{}", _now, _now);
            await _sut.InsertAsync(job1, CancellationToken.None);
            await _sut.InsertAsync(job2, CancellationToken.None);

            // Act
            var result = await _sut.GetJobByIdAsync(job1.Id, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result!.Id.Should().Be(job1.Id);
        }

        /// <summary>
        /// Verifies that GetAllSchedulesAsync returns all stored schedules ordered by job key.
        /// </summary>
        [Fact]
        public async Task GetAllSchedulesAsync_WhenSchedulesExist_ShouldReturnAllOrderedByJobKey()
        {
            // Arrange
            var schedule1 = AtomizerSchedule.Create(
                new JobKey("zebra-job"),
                QueueKey.Default,
                typeof(string),
                "payload",
                Schedule.Daily,
                TimeZoneInfo.Utc,
                _now
            );
            var schedule2 = AtomizerSchedule.Create(
                new JobKey("alpha-job"),
                QueueKey.Default,
                typeof(string),
                "payload",
                Schedule.Hourly,
                TimeZoneInfo.Utc,
                _now
            );
            await _sut.UpsertScheduleAsync(schedule1, CancellationToken.None);
            await _sut.UpsertScheduleAsync(schedule2, CancellationToken.None);

            // Act
            var all = await _sut.GetAllSchedulesAsync(CancellationToken.None);

            // Assert
            all.Should().HaveCount(2);
            all[0].JobKey.Key.Should().Be("alpha-job");
            all[1].JobKey.Key.Should().Be("zebra-job");
        }

        /// <summary>
        /// Verifies that GetAllSchedulesAsync returns disabled schedules as well as enabled ones.
        /// </summary>
        [Fact]
        public async Task GetAllSchedulesAsync_WhenDisabledScheduleExists_ShouldIncludeIt()
        {
            // Arrange
            var schedule = AtomizerSchedule.Create(
                new JobKey("disabled-job"),
                QueueKey.Default,
                typeof(string),
                "payload",
                Schedule.Daily,
                TimeZoneInfo.Utc,
                _now
            );
            schedule.Disable(_now);
            await _sut.UpsertScheduleAsync(schedule, CancellationToken.None);

            // Act
            var all = await _sut.GetAllSchedulesAsync(CancellationToken.None);

            // Assert
            all.Should().ContainSingle();
            all[0].Enabled.Should().BeFalse();
        }

        /// <summary>
        /// Verifies that GetLastJobForScheduleAsync returns the most recently updated job
        /// for the given schedule job key.
        /// </summary>
        [Fact]
        public async Task GetLastJobForScheduleAsync_WhenJobsExist_ShouldReturnMostRecentJob()
        {
            // Arrange
            var jobKey = new JobKey("my-schedule");
            var olderJob = AtomizerJob.Create(
                QueueKey.Default,
                typeof(string),
                "payload",
                _now.AddMinutes(-10),
                _now.AddMinutes(-10),
                scheduleJobKey: jobKey
            );
            olderJob.Lease(new LeaseToken("worker:*:default:*:old"), _now.AddMinutes(-10), TimeSpan.FromMinutes(5));
            olderJob.Attempt();
            olderJob.MarkAsCompleted(_now.AddMinutes(-5));

            var newerJob = AtomizerJob.Create(
                QueueKey.Default,
                typeof(string),
                "payload",
                _now,
                _now,
                scheduleJobKey: jobKey
            );
            newerJob.Lease(new LeaseToken("worker:*:default:*:new"), _now, TimeSpan.FromMinutes(5));
            newerJob.Attempt();
            newerJob.MarkAsFailed(_now);

            await _sut.InsertAsync(olderJob, CancellationToken.None);
            await _sut.InsertAsync(newerJob, CancellationToken.None);

            // Act
            var lastJob = await _sut.GetLastJobForScheduleAsync(jobKey, CancellationToken.None);

            // Assert
            lastJob.Should().NotBeNull();
            lastJob!.Id.Should().Be(newerJob.Id);
            lastJob.Status.Should().Be(AtomizerJobStatus.Failed);
        }

        /// <summary>
        /// Verifies that GetLastJobForScheduleAsync returns null when no job matches the key.
        /// </summary>
        [Fact]
        public async Task GetLastJobForScheduleAsync_WhenNoMatchingJobExists_ShouldReturnNull()
        {
            // Arrange
            var jobKey = new JobKey("nonexistent-schedule");

            // Act
            var lastJob = await _sut.GetLastJobForScheduleAsync(jobKey, CancellationToken.None);

            // Assert
            lastJob.Should().BeNull();
        }

        /// <summary>
        /// Verifies that GetLastJobForScheduleAsync ignores jobs that belong to other schedules.
        /// </summary>
        [Fact]
        public async Task GetLastJobForScheduleAsync_WhenJobsBelongToDifferentSchedule_ShouldReturnNull()
        {
            // Arrange
            var targetJobKey = new JobKey("target-schedule");
            var otherJobKey = new JobKey("other-schedule");

            var otherJob = AtomizerJob.Create(
                QueueKey.Default,
                typeof(string),
                "payload",
                _now,
                _now,
                scheduleJobKey: otherJobKey
            );
            await _sut.InsertAsync(otherJob, CancellationToken.None);

            // Act
            var lastJob = await _sut.GetLastJobForScheduleAsync(targetJobKey, CancellationToken.None);

            // Assert
            lastJob.Should().BeNull();
        }
    }
}
