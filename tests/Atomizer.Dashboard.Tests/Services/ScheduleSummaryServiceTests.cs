using Atomizer.Abstractions;
using Atomizer.Dashboard.Services;

namespace Atomizer.Dashboard.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ScheduleSummaryService"/>.
/// </summary>
public class ScheduleSummaryServiceTests
{
    private readonly IAtomizerDashboardStorage _storage = Substitute.For<IAtomizerDashboardStorage>();
    private readonly TestableLogger<ScheduleSummaryService> _logger = Substitute.For<
        TestableLogger<ScheduleSummaryService>
    >();
    private readonly ScheduleSummaryService _sut;
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    public ScheduleSummaryServiceTests()
    {
        _sut = new ScheduleSummaryService(_storage, _logger);
    }

    /// <summary>
    /// When no schedules exist the service should return an empty list without error.
    /// </summary>
    [Fact]
    public async Task GetScheduleSummariesAsync_WhenNoSchedulesExist_ShouldReturnEmpty()
    {
        // Arrange
        _storage.GetAllSchedulesAsync(CancellationToken.None).Returns(Array.Empty<AtomizerSchedule>());

        // Act
        var result = await _sut.GetScheduleSummariesAsync(CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
        await _storage.DidNotReceive().GetLastJobForScheduleAsync(Arg.Any<JobKey>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// When a schedule has never triggered its last run status should be null.
    /// </summary>
    [Fact]
    public async Task GetScheduleSummariesAsync_WhenScheduleHasNoJobs_ShouldHaveNullLastRunStatus()
    {
        // Arrange
        var schedule = AtomizerSchedule.Create(
            new JobKey("email-digest"),
            QueueKey.Default,
            typeof(string),
            "{}",
            Schedule.Daily,
            TimeZoneInfo.Utc,
            _now
        );

        _storage.GetAllSchedulesAsync(CancellationToken.None).Returns(new[] { schedule });
        _storage.GetLastJobForScheduleAsync(schedule.JobKey, CancellationToken.None).Returns((AtomizerJob?)null);

        // Act
        var result = await _sut.GetScheduleSummariesAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        var vm = result[0];
        vm.LastRunStatus.Should().BeNull();
        vm.LastRunAt.Should().BeNull();
    }

    /// <summary>
    /// When a schedule's most recent job is completed the view model should reflect that status.
    /// </summary>
    [Fact]
    public async Task GetScheduleSummariesAsync_WhenLastJobCompleted_ShouldProjectCompletedStatus()
    {
        // Arrange
        var jobKey = new JobKey("report-runner");
        var schedule = AtomizerSchedule.Create(
            jobKey,
            QueueKey.Default,
            typeof(string),
            "{}",
            Schedule.Hourly,
            TimeZoneInfo.Utc,
            _now
        );

        var completedJob = AtomizerJob.Create(
            QueueKey.Default,
            typeof(string),
            "{}",
            _now,
            _now,
            scheduleJobKey: jobKey
        );
        completedJob.Lease(new LeaseToken("worker:*:default:*:abc"), _now, TimeSpan.FromMinutes(5));
        completedJob.Attempt();
        completedJob.MarkAsCompleted(_now);

        _storage.GetAllSchedulesAsync(CancellationToken.None).Returns(new[] { schedule });
        _storage.GetLastJobForScheduleAsync(jobKey, CancellationToken.None).Returns(completedJob);

        // Act
        var result = await _sut.GetScheduleSummariesAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        var vm = result[0];
        vm.LastRunStatus.Should().Be(AtomizerJobStatus.Completed);
    }

    /// <summary>
    /// When a schedule's most recent job has failed the view model should reflect that status.
    /// </summary>
    [Fact]
    public async Task GetScheduleSummariesAsync_WhenLastJobFailed_ShouldProjectFailedStatus()
    {
        // Arrange
        var jobKey = new JobKey("sync-job");
        var schedule = AtomizerSchedule.Create(
            jobKey,
            QueueKey.Default,
            typeof(string),
            "{}",
            Schedule.EveryMinute,
            TimeZoneInfo.Utc,
            _now
        );

        var failedJob = AtomizerJob.Create(QueueKey.Default, typeof(string), "{}", _now, _now, scheduleJobKey: jobKey);
        failedJob.Lease(new LeaseToken("worker:*:default:*:xyz"), _now, TimeSpan.FromMinutes(5));
        failedJob.Attempt();
        failedJob.MarkAsFailed(_now);

        _storage.GetAllSchedulesAsync(CancellationToken.None).Returns(new[] { schedule });
        _storage.GetLastJobForScheduleAsync(jobKey, CancellationToken.None).Returns(failedJob);

        // Act
        var result = await _sut.GetScheduleSummariesAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        result[0].LastRunStatus.Should().Be(AtomizerJobStatus.Failed);
    }

    /// <summary>
    /// The projected view model should accurately surface the schedule's NextRunAt and other fields.
    /// </summary>
    [Fact]
    public async Task GetScheduleSummariesAsync_WhenScheduleExists_ShouldProjectAllFieldsCorrectly()
    {
        // Arrange
        var cronExpression = Schedule.Daily;
        var jobKey = new JobKey("nightly-cleanup");
        var queueKey = new QueueKey("high-priority");
        var createdAt = _now.AddDays(-7);

        var schedule = AtomizerSchedule.Create(
            jobKey,
            queueKey,
            typeof(string),
            "{\"count\":10}",
            cronExpression,
            TimeZoneInfo.Utc,
            createdAt
        );

        _storage.GetAllSchedulesAsync(CancellationToken.None).Returns(new[] { schedule });
        _storage.GetLastJobForScheduleAsync(jobKey, CancellationToken.None).Returns((AtomizerJob?)null);

        // Act
        var result = await _sut.GetScheduleSummariesAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        var vm = result[0];
        vm.Id.Should().Be(schedule.Id);
        vm.JobKey.Should().Be(jobKey.Key);
        vm.QueueKey.Should().Be(queueKey.Key);
        vm.CronExpression.Should().Be(cronExpression.ToString());
        vm.NextRunAt.Should().Be(schedule.NextRunAt);
        vm.Enabled.Should().BeTrue();
        vm.MisfirePolicy.Should().Be(schedule.MisfirePolicy);
        vm.PayloadTypeName.Should().Be(typeof(string).FullName);
    }

    /// <summary>
    /// When the storage call for schedules throws, the service should catch the exception,
    /// log an error, and return an empty list rather than propagating the exception.
    /// </summary>
    [Fact]
    public async Task GetScheduleSummariesAsync_WhenStorageThrows_ShouldReturnEmpty()
    {
        // Arrange
        _storage
            .GetAllSchedulesAsync(CancellationToken.None)
            .Returns<IReadOnlyList<AtomizerSchedule>>(_ => throw new InvalidOperationException("DB error"));

        // Act
        var result = await _sut.GetScheduleSummariesAsync(CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
        _logger.Received(1).LogError(Arg.Any<Exception>(), Arg.Any<string>());
    }

    /// <summary>
    /// When retrieving the last job for a specific schedule fails, that row should still be
    /// included in the result with a null LastRunStatus, and a warning should be logged.
    /// </summary>
    [Fact]
    public async Task GetScheduleSummariesAsync_WhenLastJobStorageThrows_ShouldReturnRowWithNullStatus()
    {
        // Arrange
        var jobKey = new JobKey("flaky-job");
        var schedule = AtomizerSchedule.Create(
            jobKey,
            QueueKey.Default,
            typeof(string),
            "{}",
            Schedule.Hourly,
            TimeZoneInfo.Utc,
            _now
        );

        _storage.GetAllSchedulesAsync(CancellationToken.None).Returns(new[] { schedule });
        _storage
            .GetLastJobForScheduleAsync(jobKey, CancellationToken.None)
            .Returns<AtomizerJob?>(_ => throw new InvalidOperationException("Lookup error"));

        // Act
        var result = await _sut.GetScheduleSummariesAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        result[0].LastRunStatus.Should().BeNull();
        _logger.Received(1).LogWarning(Arg.Any<Exception>(), Arg.Any<string>());
    }

    /// <summary>
    /// Multiple schedules should be projected correctly, one view model per schedule.
    /// </summary>
    [Fact]
    public async Task GetScheduleSummariesAsync_WithMultipleSchedules_ShouldProjectAllSchedules()
    {
        // Arrange
        var schedule1 = AtomizerSchedule.Create(
            new JobKey("alpha"),
            QueueKey.Default,
            typeof(string),
            "{}",
            Schedule.Hourly,
            TimeZoneInfo.Utc,
            _now
        );
        var schedule2 = AtomizerSchedule.Create(
            new JobKey("beta"),
            QueueKey.Default,
            typeof(string),
            "{}",
            Schedule.Daily,
            TimeZoneInfo.Utc,
            _now
        );

        _storage.GetAllSchedulesAsync(CancellationToken.None).Returns(new[] { schedule1, schedule2 });
        _storage.GetLastJobForScheduleAsync(Arg.Any<JobKey>(), CancellationToken.None).Returns((AtomizerJob?)null);

        // Act
        var result = await _sut.GetScheduleSummariesAsync(CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Select(vm => vm.JobKey).Should().BeEquivalentTo(new[] { "alpha", "beta" });
    }
}
