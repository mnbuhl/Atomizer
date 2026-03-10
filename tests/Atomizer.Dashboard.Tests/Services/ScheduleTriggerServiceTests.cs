using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Dashboard.Services;
using Atomizer.Tests.Utilities;

namespace Atomizer.Dashboard.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ScheduleTriggerService"/>.
/// </summary>
public class ScheduleTriggerServiceTests
{
    private readonly IAtomizerDashboardStorage _storage = Substitute.For<IAtomizerDashboardStorage>();
    private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();
    private readonly TestableLogger<ScheduleTriggerService> _logger = Substitute.For<
        TestableLogger<ScheduleTriggerService>
    >();
    private readonly ScheduleTriggerService _sut;
    private readonly DateTimeOffset _now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    public ScheduleTriggerServiceTests()
    {
        _clock.UtcNow.Returns(_now);
        _sut = new ScheduleTriggerService(_storage, _clock, _logger);
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    /// <summary>
    /// A valid trigger should insert a new job with ScheduledAt equal to the current
    /// instant and return the job ID from storage.
    /// </summary>
    [Fact]
    public async Task TriggerAsync_WhenScheduleExists_ShouldInsertJobScheduledAtNow()
    {
        // Arrange
        var schedule = BuildSchedule("report-runner");
        var expectedJobId = Guid.NewGuid();

        _storage.GetAllSchedulesAsync(CancellationToken.None).Returns(new[] { schedule });
        _storage.InsertAsync(Arg.Any<AtomizerJob>(), CancellationToken.None).Returns(expectedJobId);
        _storage
            .UpdateSchedulesAsync(Arg.Any<IEnumerable<AtomizerSchedule>>(), CancellationToken.None)
            .Returns(Task.CompletedTask);

        // Act
        var jobId = await _sut.TriggerAsync(schedule.Id, CancellationToken.None);

        // Assert
        jobId.Should().Be(expectedJobId);

        await _storage
            .Received(1)
            .InsertAsync(
                Arg.Is<AtomizerJob>(j => j.ScheduledAt == _now && j.QueueKey == schedule.QueueKey),
                CancellationToken.None
            );
    }

    /// <summary>
    /// After inserting the job, the service should update the schedule's LastEnqueueAt
    /// to the current instant so the dashboard reflects the trigger immediately.
    /// </summary>
    [Fact]
    public async Task TriggerAsync_WhenScheduleExists_ShouldUpdateLastEnqueueAt()
    {
        // Arrange
        var schedule = BuildSchedule("email-digest");

        _storage.GetAllSchedulesAsync(CancellationToken.None).Returns(new[] { schedule });
        _storage.InsertAsync(Arg.Any<AtomizerJob>(), CancellationToken.None).Returns(Guid.NewGuid());
        _storage
            .UpdateSchedulesAsync(Arg.Any<IEnumerable<AtomizerSchedule>>(), CancellationToken.None)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.TriggerAsync(schedule.Id, CancellationToken.None);

        // Assert — LastEnqueueAt should be set to now, NextRunAt must not change.
        await _storage
            .Received(1)
            .UpdateSchedulesAsync(
                Arg.Is<IEnumerable<AtomizerSchedule>>(schedules =>
                    schedules.Single().LastEnqueueAt == _now && schedules.Single().NextRunAt == schedule.NextRunAt
                ),
                CancellationToken.None
            );
    }

    /// <summary>
    /// The manual-trigger job should carry a "manual" idempotency key that includes the
    /// schedule name and the current timestamp, making each trigger distinguishable.
    /// </summary>
    [Fact]
    public async Task TriggerAsync_WhenScheduleExists_ShouldUseManualIdempotencyKey()
    {
        // Arrange
        var schedule = BuildSchedule("sync-job");
        var expectedKey = $"sync-job:manual:{_now:O}";

        _storage.GetAllSchedulesAsync(CancellationToken.None).Returns(new[] { schedule });
        _storage.InsertAsync(Arg.Any<AtomizerJob>(), CancellationToken.None).Returns(Guid.NewGuid());
        _storage
            .UpdateSchedulesAsync(Arg.Any<IEnumerable<AtomizerSchedule>>(), CancellationToken.None)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.TriggerAsync(schedule.Id, CancellationToken.None);

        // Assert
        await _storage
            .Received(1)
            .InsertAsync(Arg.Is<AtomizerJob>(j => j.IdempotencyKey == expectedKey), CancellationToken.None);
    }

    /// <summary>
    /// The job created by a manual trigger should reference the originating schedule's
    /// JobKey via ScheduleJobKey so it is traceable from the Jobs view.
    /// </summary>
    [Fact]
    public async Task TriggerAsync_WhenScheduleExists_ShouldSetScheduleJobKeyOnJob()
    {
        // Arrange
        var schedule = BuildSchedule("nightly-cleanup");

        _storage.GetAllSchedulesAsync(CancellationToken.None).Returns(new[] { schedule });
        _storage.InsertAsync(Arg.Any<AtomizerJob>(), CancellationToken.None).Returns(Guid.NewGuid());
        _storage
            .UpdateSchedulesAsync(Arg.Any<IEnumerable<AtomizerSchedule>>(), CancellationToken.None)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.TriggerAsync(schedule.Id, CancellationToken.None);

        // Assert
        await _storage
            .Received(1)
            .InsertAsync(Arg.Is<AtomizerJob>(j => j.ScheduleJobKey == schedule.JobKey), CancellationToken.None);
    }

    // ── Error paths ───────────────────────────────────────────────────────────

    /// <summary>
    /// When no schedule matches the supplied ID the service should throw
    /// <see cref="InvalidOperationException"/> without touching storage.
    /// </summary>
    [Fact]
    public async Task TriggerAsync_WhenScheduleNotFound_ShouldThrowInvalidOperationException()
    {
        // Arrange
        _storage.GetAllSchedulesAsync(CancellationToken.None).Returns(Array.Empty<AtomizerSchedule>());

        // Act
        var act = () => _sut.TriggerAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
        await _storage.DidNotReceive().InsertAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// When the schedule's PayloadType is null the service should throw
    /// <see cref="InvalidOperationException"/> and log nothing unexpected.
    /// </summary>
    [Fact]
    public async Task TriggerAsync_WhenPayloadTypeIsNull_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var schedule = BuildSchedule("no-payload-type");
        schedule.PayloadType = null; // Simulate an unresolved type.

        _storage.GetAllSchedulesAsync(CancellationToken.None).Returns(new[] { schedule });

        // Act
        var act = () => _sut.TriggerAsync(schedule.Id, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*PayloadType*");
        await _storage.DidNotReceive().InsertAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// When storage throws during InsertAsync the exception should propagate to the caller.
    /// </summary>
    [Fact]
    public async Task TriggerAsync_WhenInsertThrows_ShouldPropagateException()
    {
        // Arrange
        var schedule = BuildSchedule("flaky-job");

        _storage.GetAllSchedulesAsync(CancellationToken.None).Returns(new[] { schedule });
        _storage
            .InsertAsync(Arg.Any<AtomizerJob>(), CancellationToken.None)
            .Returns<Task<Guid>>(_ => throw new InvalidOperationException("DB error"));

        // Act
        var act = () => _sut.TriggerAsync(schedule.Id, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("DB error");
    }

    /// <summary>
    /// A successful trigger should produce an Information log entry that includes
    /// the schedule's job key and the newly enqueued job ID.
    /// </summary>
    [Fact]
    public async Task TriggerAsync_WhenSuccessful_ShouldLogInformation()
    {
        // Arrange
        var schedule = BuildSchedule("audit-logger");
        var jobId = Guid.NewGuid();

        _storage.GetAllSchedulesAsync(CancellationToken.None).Returns(new[] { schedule });
        _storage.InsertAsync(Arg.Any<AtomizerJob>(), CancellationToken.None).Returns(jobId);
        _storage
            .UpdateSchedulesAsync(Arg.Any<IEnumerable<AtomizerSchedule>>(), CancellationToken.None)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.TriggerAsync(schedule.Id, CancellationToken.None);

        // Assert
        _logger.Received(1).LogInformation(Arg.Any<string>());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal <see cref="AtomizerSchedule"/> with a resolvable
    /// <see cref="AtomizerSchedule.PayloadType"/> suitable for trigger tests.
    /// </summary>
    private AtomizerSchedule BuildSchedule(string jobKeyName) =>
        AtomizerSchedule.Create(
            new JobKey(jobKeyName),
            QueueKey.Default,
            typeof(string),
            "{\"data\":\"test\"}",
            Schedule.Hourly,
            TimeZoneInfo.Utc,
            _now.AddDays(-1)
        );
}
