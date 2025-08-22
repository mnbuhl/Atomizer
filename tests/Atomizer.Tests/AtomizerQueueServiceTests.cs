using Atomizer.Processing;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Atomizer.Tests
{
    /// <summary>
    /// Unit tests for AtomizerQueueService.
    /// </summary>
    public class AtomizerQueueServiceTests
    {
        [Fact]
        public async Task StartAsync_WhenStartupDelayIsSet_ShouldDelayAndStartCoordinator()
        {
            // Arrange
            var coordinator = Substitute.For<IQueueCoordinator>();
            var options = new AtomizerProcessingOptions { StartupDelay = TimeSpan.FromMilliseconds(50) };
            var logger = Substitute.For<ILogger<AtomizerQueueService>>();
            var service = new AtomizerQueueService(coordinator, options, logger);
            var token = CancellationToken.None;

            // Act
            var start = DateTime.UtcNow;
            await service.StartAsync(token);
            var elapsed = DateTime.UtcNow - start;

            // Assert
            elapsed.Should().BeGreaterThanOrEqualTo(options.StartupDelay.Value);
            coordinator.Received(1).Start(token);
            logger.Received().LogInformation(Arg.Is<string>(s => s.Contains("starting")));
        }

        [Fact]
        public async Task StartAsync_WhenStartupDelayIsNull_ShouldStartCoordinatorWithoutDelay()
        {
            // Arrange
            var coordinator = Substitute.For<IQueueCoordinator>();
            var options = new AtomizerProcessingOptions { StartupDelay = null };
            var logger = Substitute.For<ILogger<AtomizerQueueService>>();
            var service = new AtomizerQueueService(coordinator, options, logger);
            var token = CancellationToken.None;

            // Act
            await service.StartAsync(token);

            // Assert
            coordinator.Received(1).Start(token);
            logger.Received().LogInformation(Arg.Is<string>(s => s.Contains("starting")));
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
            logger.Received().LogInformation(Arg.Is<string>(s => s.Contains("stopping")));
            logger.Received().LogInformation(Arg.Is<string>(s => s.Contains("stopped")));
        }
    }
}
