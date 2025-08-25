using Atomizer.Scheduling;

namespace Atomizer.Tests.Scheduling
{
    /// <summary>
    /// Unit tests for <see cref="Scheduler"/>.
    /// </summary>
    public class SchedulerTests
    {
        private readonly ISchedulePoller _poller = Substitute.For<ISchedulePoller>();
        private readonly TestableLogger<Scheduler> _logger = Substitute.For<TestableLogger<Scheduler>>();
        private readonly Scheduler _sut;

        public SchedulerTests()
        {
            _sut = new Scheduler(_logger, _poller);
        }

        [Fact]
        public async Task Start_WhenCalled_ShouldStartSchedulePollerAndLog()
        {
            // Arrange
            var token = CancellationToken.None;

            // Act
            _sut.Start(token);

            await Task.Delay(50, TestContext.Current.CancellationToken); // Give some time for the async task to star

            // Assert
            _logger.Received(1).LogInformation("Starting Atomizer Scheduler");
            await _poller.Received(1).RunAsync(Arg.Any<CancellationToken>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task StopAsync_WhenCalled_ShouldStopSchedulePollerAndLog()
        {
            // Arrange
            var grace = TimeSpan.FromMilliseconds(50);
            var token = CancellationToken.None;
            _poller.ReleaseLeasedSchedulesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            _sut.Start(token);

            // Act
            await _sut.StopAsync(grace, token);

            // Assert
            _logger.Received(1).LogInformation("Stopping Atomizer Scheduler");
            _poller.Received(1).ReleaseLeasedSchedulesAsync(Arg.Any<CancellationToken>());
            _logger.Received(1).LogInformation("Atomizer Scheduler stopped");
        }

        [Fact]
        public async Task StopAsync_WhenReleaseLeasedSchedulesThrows_ShouldDisposeAndLog()
        {
            // Arrange
            var grace = TimeSpan.FromMilliseconds(50);
            var token = CancellationToken.None;
            _poller.ReleaseLeasedSchedulesAsync(Arg.Any<CancellationToken>()).Returns(_ => throw new Exception("fail"));
            _sut.Start(token);

            // Act
            await _sut.StopAsync(grace, token);

            // Assert
            _logger.Received(1).LogError(Arg.Any<Exception>(), "Release leased schedules operation failed");
            _logger.Received(1).LogInformation("Atomizer Scheduler stopped");
        }
    }
}
