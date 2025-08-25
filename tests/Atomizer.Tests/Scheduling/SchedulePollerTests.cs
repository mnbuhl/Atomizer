using System.Text.Json;
using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Scheduling;
using Atomizer.Tests.TestJobs;

namespace Atomizer.Tests.Scheduling
{
    /// <summary>
    /// Unit tests for <see cref="SchedulePoller"/>.
    /// </summary>
    public class SchedulePollerTests
    {
        private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();
        private readonly IAtomizerStorageScopeFactory _storageScopeFactory =
            Substitute.For<IAtomizerStorageScopeFactory>();
        private readonly TestableLogger<SchedulePoller> _logger = Substitute.For<TestableLogger<SchedulePoller>>();
        private readonly IScheduleProcessor _scheduleProcessor = Substitute.For<IScheduleProcessor>();
        private readonly AtomizerRuntimeIdentity _identity = new AtomizerRuntimeIdentity();
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
            _sut = new SchedulePoller(
                atomizerOptions,
                _clock,
                _storageScopeFactory,
                _logger,
                _identity,
                _scheduleProcessor
            );
        }

        [Fact]
        public async Task RunAsync_WhenDueSchedules_ShouldProcessSchedules()
        {
            // Arrange
            var ioCts = new CancellationTokenSource();
            var execCts = new CancellationTokenSource();
            var scope = Substitute.For<IAtomizerStorageScope>();
            var storage = Substitute.For<IAtomizerStorage>();
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
            _storageScopeFactory.CreateScope().Returns(scope);
            storage
                .LeaseDueSchedulesAsync(
                    Arg.Any<DateTimeOffset>(),
                    Arg.Any<TimeSpan>(),
                    Arg.Any<LeaseToken>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns([schedule1, schedule2]);
            _scheduleProcessor
                .ProcessAsync(Arg.Any<AtomizerSchedule>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            // Act
            var runTask = _sut.RunAsync(ioCts.Token, execCts.Token);
            await Task.Delay(50, TestContext.Current.CancellationToken); // allow poller to run
            await ioCts.CancelAsync();
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
            await ioCts.CancelAsync();

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
            var scope = Substitute.For<IAtomizerStorageScope>();
            scope.Storage.Returns(_ => throw new InvalidOperationException("fail"));
            _storageScopeFactory.CreateScope().Returns(scope);

            // Act
            var runTask = _sut.RunAsync(ioCts.Token, execCts.Token);
            await Task.Delay(20, TestContext.Current.CancellationToken);
            await ioCts.CancelAsync();
            await runTask;

            // Assert
            _logger.Received().LogError(Arg.Any<Exception>(), "An error occurred while polling the schedule");
        }

        [Fact]
        public async Task ReleaseLeasedSchedulesAsync_WhenCalled_ShouldReleaseAndLog()
        {
            // Arrange
            var scope = Substitute.For<IAtomizerStorageScope>();
            var storage = Substitute.For<IAtomizerStorage>();
            scope.Storage.Returns(storage);
            _storageScopeFactory.CreateScope().Returns(scope);
            storage.ReleaseLeasedSchedulesAsync(Arg.Any<LeaseToken>(), Arg.Any<CancellationToken>()).Returns(1);

            // Act
            await _sut.ReleaseLeasedSchedulesAsync(CancellationToken.None);

            // Assert
            _logger.Received().LogDebug(Arg.Is<string>(s => s.Contains("Released 1 schedule(s) with lease token")));
        }

        [Fact]
        public async Task ReleaseLeasedSchedulesAsync_WhenExceptionThrown_ShouldLogError()
        {
            // Arrange
            var scope = Substitute.For<IAtomizerStorageScope>();
            scope.Storage.Returns(_ => throw new InvalidOperationException("fail"));
            _storageScopeFactory.CreateScope().Returns(scope);

            // Act
            await _sut.ReleaseLeasedSchedulesAsync(CancellationToken.None);

            // Assert
            _logger
                .Received()
                .LogError(
                    Arg.Any<Exception>(),
                    Arg.Is<string>(s => s.Contains("Failed to release schedules with lease token"))
                );
        }
    }
}
