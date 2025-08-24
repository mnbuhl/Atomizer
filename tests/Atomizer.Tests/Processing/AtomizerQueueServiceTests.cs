using System.Diagnostics;
using Atomizer.Processing;
using Microsoft.Extensions.Logging;

namespace Atomizer.Tests.Processing
{
    /// <summary>
    /// Unit tests for AtomizerQueueService.
    /// </summary>
    public class AtomizerQueueServiceTests
    {
        [Fact]
        public async Task ExecuteAsync_WhenStartupDelayIsSet_ShouldDelayAndStartCoordinator()
        {
            // Arrange
            var coordinator = Substitute.For<IQueueCoordinator>();
            var options = new AtomizerProcessingOptions { StartupDelay = TimeSpan.FromMilliseconds(50) };
            var logger = Substitute.For<ILogger<AtomizerQueueService>>();
            var service = new AtomizerQueueService(coordinator, options, logger);
            var token = CancellationToken.None;

            var executeAsync = NonPublicSpy<AtomizerQueueService>.CreateFunc<CancellationToken, Task>("ExecuteAsync");

            // Act
            var timestamp = Stopwatch.GetTimestamp();
            await executeAsync(service, token);
            var elapsed = Stopwatch.GetElapsedTime(timestamp);

            // Assert
            elapsed.Should().BeGreaterThanOrEqualTo(options.StartupDelay.Value);
            coordinator.Received(1).Start(token);
            logger.Received(1).LogInformation("Atomizer queue service starting");
        }

        [Fact]
        public async Task ExecuteAsync_WhenStartupDelayIsNull_ShouldStartCoordinatorWithoutDelay()
        {
            // Arrange
            var coordinator = Substitute.For<IQueueCoordinator>();
            var options = new AtomizerProcessingOptions { StartupDelay = null };
            var logger = Substitute.For<ILogger<AtomizerQueueService>>();
            var service = new AtomizerQueueService(coordinator, options, logger);
            var token = CancellationToken.None;

            var executeAsync = NonPublicSpy<AtomizerQueueService>.CreateFunc<CancellationToken, Task>("ExecuteAsync");

            // Act
            await executeAsync(service, token);

            // Assert
            coordinator.Received(1).Start(token);
            logger.Received(1).LogInformation("Atomizer queue service starting");
        }

        [Fact]
        public async Task StopAsync_WhenCalled_ShouldStopCoordinatorAndLog()
        {
            // Arrange
            var coordinator = Substitute.For<IQueueCoordinator>();
            var options = new AtomizerProcessingOptions { GracefulShutdownTimeout = TimeSpan.FromSeconds(1) };
            var logger = Substitute.For<ILogger<AtomizerQueueService>>();
            var service = new AtomizerQueueService(coordinator, options, logger);
            var token = CancellationToken.None;

            // Act
            await service.StopAsync(token);

            // Assert
            await coordinator.Received(1).StopAsync(options.GracefulShutdownTimeout, token);
            logger.Received(1).LogInformation("Atomizer queue service stopping");
            logger.Received(1).LogInformation("Atomizer queue service stopped");
        }
    }
}
