using System.Text.Json;
using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Scheduling;
using Atomizer.Tests.Utilities.TestJobs;

namespace Atomizer.Tests.Scheduling;

/// <summary>
/// Unit tests for <see cref="SchedulePoller"/>.
/// </summary>
public class SchedulePollerTests
{
    private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();
    private readonly IAtomizerServiceScopeFactory _serviceScopeFactory = Substitute.For<IAtomizerServiceScopeFactory>();
    private readonly TestableLogger<SchedulePoller> _logger = Substitute.For<TestableLogger<SchedulePoller>>();
    private readonly IScheduleProcessor _scheduleProcessor = Substitute.For<IScheduleProcessor>();
    private readonly SchedulePoller _sut;

    public SchedulePollerTests()
    {
        var options = new SchedulingOptions
        {
            StorageCheckInterval = TimeSpan.FromMilliseconds(10),
            ScheduleLeadTime = TimeSpan.FromMilliseconds(10),
            VisibilityTimeout = TimeSpan.FromMilliseconds(10),
            TickInterval = TimeSpan.FromMilliseconds(5),
        };
        var atomizerOptions = new AtomizerOptions();
        atomizerOptions.SchedulingOptions = options;
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _clock.MinValue.Returns(DateTimeOffset.MinValue);
        _sut = new SchedulePoller(atomizerOptions, _clock, _serviceScopeFactory, _logger, _scheduleProcessor);
    }

    [Fact]
    public async Task RunAsync_WhenDueSchedules_ShouldProcessSchedules()
    {
        // Arrange
        var ioCts = new CancellationTokenSource();
        var execCts = new CancellationTokenSource();
        var scope = Substitute.For<IAtomizerServiceScope>();
        var storage = Substitute.For<IAtomizerStorage>();
        var leasingScopeFactory = Substitute.For<IAtomizerLeasingScopeFactory>();
        var leasingScope = Substitute.For<IAtomizerLeasingScope>();
        leasingScopeFactory
            .CreateScopeAsync(Arg.Any<QueueKey>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(leasingScope);
        var schedule1 = AtomizerSchedule.Create(
            "testjob",
            QueueKey.Default,
            typeof(WriteLineMessage),
            JsonSerializer.Serialize(new WriteLineMessage("Hello 1")),
            Schedule.EverySecond,
            TimeZoneInfo.Utc,
            _clock.UtcNow
        );
        var schedule2 = AtomizerSchedule.Create(
            "testjob2",
            QueueKey.Default,
            typeof(WriteLineMessage),
            JsonSerializer.Serialize(new WriteLineMessage("Hello 2")),
            Schedule.EverySecond,
            TimeZoneInfo.Utc,
            _clock.UtcNow
        );
        scope.Storage.Returns(storage);
        scope.LeasingScopeFactory.Returns(leasingScopeFactory);
        leasingScope.Acquired.Returns(true);
        _serviceScopeFactory.CreateScope().Returns(scope);
        storage
            .GetDueSchedulesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new[] { schedule1, schedule2 });
        _scheduleProcessor
            .ProcessAsync(Arg.Any<AtomizerSchedule>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var runTask = _sut.RunAsync(ioCts.Token, execCts.Token);
        await Task.Delay(50, TestContext.Current.CancellationToken); // allow poller to run
        ioCts.Cancel();
        await runTask;

        // Assert
        await _scheduleProcessor.Received(1).ProcessAsync(schedule1, Arg.Any<DateTimeOffset>(), execCts.Token);
        await _scheduleProcessor.Received(1).ProcessAsync(schedule2, Arg.Any<DateTimeOffset>(), execCts.Token);
    }

    [Fact]
    public async Task RunAsync_WhenIoTokenCancelled_ShouldNotThrow()
    {
        // Arrange
        var ioCts = new CancellationTokenSource();
        var execCts = new CancellationTokenSource();
        ioCts.Cancel();

        // Act
        var act = async () => await _sut.RunAsync(ioCts.Token, execCts.Token);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunAsync_WhenExceptionThrown_ShouldLogError()
    {
        // Arrange
        var ioCts = new CancellationTokenSource();
        var execCts = new CancellationTokenSource();
        var scope = Substitute.For<IAtomizerServiceScope>();
        scope.LeasingScopeFactory.Returns(_ => throw new InvalidOperationException("fail"));
        _serviceScopeFactory.CreateScope().Returns(scope);

        // Act
        var runTask = _sut.RunAsync(ioCts.Token, execCts.Token);
        await Task.Delay(20, TestContext.Current.CancellationToken);
        ioCts.Cancel();
        await runTask;

        // Assert
        _logger.Received().LogError(Arg.Any<Exception>(), "An error occurred while polling the schedule");
    }
}
