using Atomizer.Processing;

namespace Atomizer.Tests.Processing
{
    /// <summary>
    /// Unit tests for <see cref="QueueCoordinator"/>.
    /// </summary>
    public class QueueCoordinatorTests
    {
        /// <summary>
        /// Verifies that Start creates and starts a pump for each queue and logs the correct message.
        /// </summary>
        [Fact]
        public void Start_WhenCalled_ShouldCreateAndStartAllQueuePumps()
        {
            // Arrange
            var options = new AtomizerOptions();
            options.AddQueue(
                "queue1",
                q =>
                {
                    q.BatchSize = 1;
                    q.DegreeOfParallelism = 1;
                    q.VisibilityTimeout = TimeSpan.FromSeconds(1);
                }
            );
            options.AddQueue(
                "queue2",
                q =>
                {
                    q.BatchSize = 1;
                    q.DegreeOfParallelism = 1;
                    q.VisibilityTimeout = TimeSpan.FromSeconds(1);
                }
            );

            var logger = Substitute.For<TestableLogger<QueueCoordinator>>();
            var pumpFactory = Substitute.For<IQueuePumpFactory>();
            var pump1 = Substitute.For<IQueuePump>();
            var pump2 = Substitute.For<IQueuePump>();

            pumpFactory.Create(options.Queues[0]).Returns(pump1);
            pumpFactory.Create(options.Queues[1]).Returns(pump2);

            var coordinator = new QueueCoordinator(options, logger, pumpFactory);
            var ct = CancellationToken.None;

            // Act
            coordinator.Start(ct);

            // Assert
            pumpFactory.Received(1).Create(options.Queues[0]);
            pumpFactory.Received(1).Create(options.Queues[1]);
            pump1.Received(1).Start(ct);
            pump2.Received(1).Start(ct);
            logger.Received(1).LogInformation($"Starting {options.Queues.Count} queue pump(s)...");
        }

        /// <summary>
        /// Verifies that StopAsync calls StopAsync on all pumps and logs the correct message.
        /// </summary>
        [Fact]
        public async Task StopAsync_WhenCalled_ShouldStopAllQueuePumpsAndLog()
        {
            // Arrange
            var options = new AtomizerOptions();
            options.AddQueue(
                "queue1",
                q =>
                {
                    q.BatchSize = 1;
                    q.DegreeOfParallelism = 1;
                    q.VisibilityTimeout = TimeSpan.FromSeconds(1);
                }
            );
            options.AddQueue(
                "queue2",
                q =>
                {
                    q.BatchSize = 1;
                    q.DegreeOfParallelism = 1;
                    q.VisibilityTimeout = TimeSpan.FromSeconds(1);
                }
            );

            var logger = Substitute.For<TestableLogger<QueueCoordinator>>();
            var pumpFactory = Substitute.For<IQueuePumpFactory>();
            var pump1 = Substitute.For<IQueuePump>();
            var pump2 = Substitute.For<IQueuePump>();

            pumpFactory.Create(options.Queues[0]).Returns(pump1);
            pumpFactory.Create(options.Queues[1]).Returns(pump2);

            var coordinator = new QueueCoordinator(options, logger, pumpFactory);
            var ct = CancellationToken.None;
            coordinator.Start(ct);

            pump1.StopAsync(Arg.Any<TimeSpan>(), ct).Returns(Task.CompletedTask);
            pump2.StopAsync(Arg.Any<TimeSpan>(), ct).Returns(Task.CompletedTask);

            // Act
            await coordinator.StopAsync(TimeSpan.FromSeconds(1), ct);

            // Assert
            await pump1.Received(1).StopAsync(TimeSpan.FromSeconds(1), ct);
            await pump2.Received(1).StopAsync(TimeSpan.FromSeconds(1), ct);
            logger.Received(1).LogInformation("All queue pumps stopped");
        }
    }
}
