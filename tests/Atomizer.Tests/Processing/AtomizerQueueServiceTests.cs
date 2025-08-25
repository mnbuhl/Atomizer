using System.Diagnostics;
using Atomizer.Processing;

namespace Atomizer.Tests.Processing
{
    /// <summary>
    /// Unit tests for AtomizerQueueService.
    /// </summary>
    public class AtomizerQueueServiceTests
    {
        private readonly IQueueCoordinator _coordinator = Substitute.For<IQueueCoordinator>();
        private readonly AtomizerProcessingOptions _options = new AtomizerProcessingOptions();
        private readonly TestableLogger<AtomizerQueueService> _logger = Substitute.For<
            TestableLogger<AtomizerQueueService>
        >();

        private readonly AtomizerQueueService _sut;

        public AtomizerQueueServiceTests()
        {
            _sut = new AtomizerQueueService(_coordinator, _options, _logger);
        }

        [Fact]
        public async Task ExecuteAsync_WhenStartupDelayIsSet_ShouldDelayAndStartCoordinator()
        {
            // Arrange
            _options.StartupDelay = TimeSpan.FromMilliseconds(50);
            var token = CancellationToken.None;

            var executeAsync = NonPublicSpy.CreateFunc<AtomizerQueueService, CancellationToken, Task>("ExecuteAsync");

            // Act
            var timestamp = DateTimeOffset.UtcNow;
            await executeAsync(_sut, token);
            var elapsed = DateTimeOffset.UtcNow - timestamp;

            // Assert
            elapsed.Should().BeGreaterThanOrEqualTo(_options.StartupDelay.Value);
            _coordinator.Received(1).Start(token);
            _logger.Received(1).LogInformation("Atomizer queue service starting");
        }

        [Fact]
        public async Task ExecuteAsync_WhenStartupDelayIsNull_ShouldStartCoordinatorWithoutDelay()
        {
            // Arrange
            _options.StartupDelay = null;
            var token = CancellationToken.None;

            var executeAsync = NonPublicSpy.CreateFunc<AtomizerQueueService, CancellationToken, Task>("ExecuteAsync");

            // Act
            await executeAsync(_sut, token);

            // Assert
            _coordinator.Received(1).Start(token);
            _logger.Received(1).LogInformation("Atomizer queue service starting");
        }

        [Fact]
        public async Task StopAsync_WhenCalled_ShouldStopCoordinatorAndLog()
        {
            // Arrange
            _options.GracefulShutdownTimeout = TimeSpan.FromSeconds(1);
            var token = CancellationToken.None;

            // Act
            await _sut.StopAsync(token);

            // Assert
            await _coordinator.Received(1).StopAsync(_options.GracefulShutdownTimeout, token);
            _logger.Received(1).LogInformation("Atomizer queue service stopping");
            _logger.Received(1).LogInformation("Atomizer queue service stopped");
        }
    }
}
