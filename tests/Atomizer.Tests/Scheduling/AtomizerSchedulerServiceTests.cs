using System.Diagnostics;
using Atomizer.Scheduling;

namespace Atomizer.Tests.Scheduling
{
    /// <summary>
    /// Unit tests for AtomizerSchedulerService.
    /// </summary>
    public class AtomizerSchedulerServiceTests
    {
        private readonly IScheduler _scheduler = Substitute.For<IScheduler>();
        private readonly AtomizerProcessingOptions _options = new AtomizerProcessingOptions();
        private readonly TestableLogger<AtomizerSchedulerService> _logger = Substitute.For<
            TestableLogger<AtomizerSchedulerService>
        >();

        private readonly AtomizerSchedulerService _sut;

        public AtomizerSchedulerServiceTests()
        {
            _sut = new AtomizerSchedulerService(_scheduler, _options, _logger);
        }

        [Fact]
        public async Task ExecuteAsync_WhenStartupDelayIsSet_ShouldDelayAndStartScheduler()
        {
            // Arrange
            _options.StartupDelay = TimeSpan.FromMilliseconds(50);
            var token = CancellationToken.None;

            var executeAsync = NonPublicSpy.CreateFunc<AtomizerSchedulerService, CancellationToken, Task>(
                "ExecuteAsync"
            );

            // Act
            var timestamp = Stopwatch.GetTimestamp();
            await executeAsync(_sut, token);
            var elapsed = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - timestamp);

            // Assert
            elapsed.Should().BeGreaterThanOrEqualTo(_options.StartupDelay.Value);
            _scheduler.Received(1).Start(token);
            _logger.Received(1).LogInformation("Atomizer scheduler service starting");
        }

        [Fact]
        public async Task ExecuteAsync_WhenStartupDelayIsNull_ShouldStartSchedulerWithoutDelay()
        {
            // Arrange
            _options.StartupDelay = null;
            var token = CancellationToken.None;

            var executeAsync = NonPublicSpy.CreateFunc<AtomizerSchedulerService, CancellationToken, Task>(
                "ExecuteAsync"
            );

            // Act
            await executeAsync(_sut, token);

            // Assert
            _scheduler.Received(1).Start(token);
            _logger.Received(1).LogInformation("Atomizer scheduler service starting");
        }

        [Fact]
        public async Task StopAsync_WhenCalled_ShouldStopSchedulerAndLog()
        {
            // Arrange
            _options.GracefulShutdownTimeout = TimeSpan.FromSeconds(1);
            var token = CancellationToken.None;

            // Act
            await _sut.StopAsync(token);

            // Assert
            await _scheduler.Received(1).StopAsync(_options.GracefulShutdownTimeout, token);
            _logger.Received(1).LogInformation("Atomizer scheduler service stopping");
            _logger.Received(1).LogInformation("Atomizer scheduler service stopped");
        }
    }
}
