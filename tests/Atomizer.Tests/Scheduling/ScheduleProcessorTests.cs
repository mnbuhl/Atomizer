using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Scheduling;
using Atomizer.Storage;
using Atomizer.Tests.Utilities.TestJobs;

namespace Atomizer.Tests.Scheduling;

/// <summary>
/// Unit tests for <see cref="ScheduleProcessor"/>.
/// </summary>
public class ScheduleProcessorTests
{
    private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();
    private readonly IAtomizerStorageScopeFactory _storageScopeFactory = Substitute.For<IAtomizerStorageScopeFactory>();
    private readonly TestableLogger<ScheduleProcessor> _logger = Substitute.For<TestableLogger<ScheduleProcessor>>();
    private readonly ScheduleProcessor _sut;
    private readonly IAtomizerStorageScope _scope = Substitute.For<IAtomizerStorageScope>();
    private readonly IAtomizerStorage _storage = Substitute.For<IAtomizerStorage>();

    public ScheduleProcessorTests()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _scope.Storage.Returns(_storage);
        _storageScopeFactory.CreateScope().Returns(_scope);
        _sut = new ScheduleProcessor(_clock, _storageScopeFactory, _logger);
    }

    [Fact]
    public async Task ProcessAsync_WhenOccurrencesExist_ShouldInsertJobs()
    {
        // Arrange
        var schedule = AtomizerSchedule.Create(
            new JobKey("testjob"),
            QueueKey.Default,
            typeof(WriteLineMessage),
            "payload",
            Schedule.EverySecond,
            TimeZoneInfo.Utc,
            _clock.UtcNow
        );
        var horizon = _clock.UtcNow.AddSeconds(2);
        var token = CancellationToken.None;
        var occurrences = schedule.GetOccurrences(horizon);
        _storage
            .AcquireLockAsync(QueueKey.Scheduler, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new NoopLock());

        // Act
        await _sut.ProcessAsync(schedule, horizon, token);

        // Assert
        foreach (var occurrence in occurrences)
        {
            await _storage
                .Received()
                .InsertAsync(
                    Arg.Is<AtomizerJob>(j => j.ScheduledAt == occurrence && j.Payload == schedule.Payload),
                    token
                );
        }
    }

    [Fact]
    public async Task ProcessAsync_WhenInsertJobThrows_ShouldLogErrorAndContinue()
    {
        // Arrange
        var schedule = AtomizerSchedule.Create(
            new JobKey("testjob"),
            QueueKey.Default,
            typeof(WriteLineMessage),
            "payload",
            Schedule.EverySecond,
            TimeZoneInfo.Utc,
            _clock.UtcNow
        );
        var horizon = _clock.UtcNow.AddSeconds(2);
        var token = CancellationToken.None;

        _storage.InsertAsync(Arg.Any<AtomizerJob>(), token).Returns<Task>(_ => throw new Exception("Insert failed"));
        _storage
            .AcquireLockAsync(QueueKey.Scheduler, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new NoopLock());

        // Act
        await _sut.ProcessAsync(schedule, horizon, token);

        // Assert
        _logger
            .Received(1)
            .LogError(
                Arg.Any<Exception>(),
                Arg.Is<string>(s => s.StartsWith($"Failed to insert scheduled job {schedule.JobKey}"))
            );
    }
}
