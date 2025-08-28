using System.Diagnostics;
using System.Threading.Channels;
using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Processing;

namespace Atomizer.Tests.Processing;

/// <summary>
/// Unit tests for the QueuePump class.
/// </summary>
public class QueuePumpTests
{
    [Fact]
    public async Task Start_WhenCalled_ShouldLogAndStartWorkersAndPoller()
    {
        // Arrange
        var queueOptions = new QueueOptions(QueueKey.Default) { DegreeOfParallelism = 2, BatchSize = 1 };
        var poller = Substitute.For<IQueuePoller>();
        var storageScopeFactory = Substitute.For<IAtomizerServiceScopeFactory>();
        var logger = Substitute.For<TestableLogger<QueuePump>>();
        var workerFactory = Substitute.For<IJobWorkerFactory>();
        var identity = new AtomizerRuntimeIdentity();
        var worker = Substitute.For<IJobWorker>();
        var clock = Substitute.For<IAtomizerClock>();
        workerFactory.Create(Arg.Any<QueueKey>(), Arg.Any<int>()).Returns(worker);
        worker
            .RunAsync(Arg.Any<ChannelReader<AtomizerJob>>(), Arg.Any<CancellationToken>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        poller
            .RunAsync(
                Arg.Any<QueueOptions>(),
                Arg.Any<LeaseToken>(),
                Arg.Any<Channel<AtomizerJob>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        var pump = new QueuePump(queueOptions, poller, storageScopeFactory, logger, workerFactory, identity, clock);

        // Act
        pump.Start(CancellationToken.None);

        await Task.Delay(100, TestContext.Current.CancellationToken); // Give some time for async operations to start

        // Assert
        logger
            .Received()
            .LogInformation(
                $"Starting queue '{queueOptions.QueueKey}' with {queueOptions.DegreeOfParallelism} workers"
            );
        workerFactory.Received(2).Create(Arg.Any<QueueKey>(), Arg.Any<int>());
        await poller
            .Received()
            .RunAsync(
                Arg.Any<QueueOptions>(),
                Arg.Any<LeaseToken>(),
                Arg.Any<Channel<AtomizerJob>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task StopAsync_WhenCalled_ShouldLogAndCancelTokensAndWaitForWorkers()
    {
        // Arrange
        var queueOptions = new QueueOptions(QueueKey.Default) { DegreeOfParallelism = 2, BatchSize = 1 };
        var poller = Substitute.For<IQueuePoller>();
        var storageScopeFactory = Substitute.For<IAtomizerServiceScopeFactory>();
        var logger = Substitute.For<TestableLogger<QueuePump>>();
        var workerFactory = Substitute.For<IJobWorkerFactory>();
        var identity = new AtomizerRuntimeIdentity();
        var worker = Substitute.For<IJobWorker>();
        var clock = Substitute.For<IAtomizerClock>();
        workerFactory.Create(Arg.Any<QueueKey>(), Arg.Any<int>()).Returns(worker);
        worker
            .RunAsync(Arg.Any<ChannelReader<AtomizerJob>>(), Arg.Any<CancellationToken>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        poller
            .RunAsync(
                Arg.Any<QueueOptions>(),
                Arg.Any<LeaseToken>(),
                Arg.Any<Channel<AtomizerJob>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        var pump = new QueuePump(queueOptions, poller, storageScopeFactory, logger, workerFactory, identity, clock);
        pump.Start(CancellationToken.None);

        // Act
        await pump.StopAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        // Assert
        logger.Received().LogInformation($"Stopping queue '{queueOptions.QueueKey}'...");
        logger.Received().LogInformation($"Queue '{queueOptions.QueueKey}' stopped");
    }

    [Fact]
    public async Task StopAsync_WhenWorkerIsLongRunning_ShouldRespectGracePeriod()
    {
        // Arrange
        var queueOptions = new QueueOptions(QueueKey.Default) { DegreeOfParallelism = 1, BatchSize = 1 };
        var poller = Substitute.For<IQueuePoller>();
        var storageScopeFactory = Substitute.For<IAtomizerServiceScopeFactory>();
        var storageScope = Substitute.For<IAtomizerServiceScope>();
        var storage = Substitute.For<IAtomizerStorage>();
        storageScope.Storage.Returns(storage);
        storageScopeFactory.CreateScope().Returns(storageScope);
        var logger = Substitute.For<TestableLogger<QueuePump>>();
        var workerFactory = Substitute.For<IJobWorkerFactory>();
        var identity = new AtomizerRuntimeIdentity();
        var worker = Substitute.For<IJobWorker>();
        var clock = Substitute.For<IAtomizerClock>();
        workerFactory.Create(Arg.Any<QueueKey>(), Arg.Any<int>()).Returns(worker);
        // Simulate a worker that takes 3 seconds to complete
        worker
            .RunAsync(Arg.Any<ChannelReader<AtomizerJob>>(), Arg.Any<CancellationToken>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
            });
        poller
            .RunAsync(
                Arg.Any<QueueOptions>(),
                Arg.Any<LeaseToken>(),
                Arg.Any<Channel<AtomizerJob>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        var pump = new QueuePump(queueOptions, poller, storageScopeFactory, logger, workerFactory, identity, clock);
        pump.Start(CancellationToken.None);

        // Act
        var sw = Stopwatch.StartNew();
        await pump.StopAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        sw.Stop();

        // Assert: Should return after about 1 second, not 2
        sw.Elapsed.TotalSeconds.Should().BeGreaterThanOrEqualTo(1);
        sw.Elapsed.TotalSeconds.Should()
            .BeLessThanOrEqualTo(
                1.5,
                $"StopAsync should return after about 1 second, elapsed: {sw.Elapsed.TotalSeconds}"
            );

        logger.Received(1).LogInformation($"Stopping queue '{queueOptions.QueueKey}'...");
        logger.Received(1).LogInformation($"Queue '{queueOptions.QueueKey}' stopped");

        await storage
            .Received(1)
            .ReleaseLeasedAsync(Arg.Any<LeaseToken>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_WhenCalledBeforeStart_ShouldNotThrow()
    {
        // Arrange
        var queueOptions = new QueueOptions(QueueKey.Default) { DegreeOfParallelism = 1, BatchSize = 1 };
        var poller = Substitute.For<IQueuePoller>();
        var storageScopeFactory = Substitute.For<IAtomizerServiceScopeFactory>();
        var logger = Substitute.For<TestableLogger<QueuePump>>();
        var workerFactory = Substitute.For<IJobWorkerFactory>();
        var identity = new AtomizerRuntimeIdentity();
        var clock = Substitute.For<IAtomizerClock>();

        var pump = new QueuePump(queueOptions, poller, storageScopeFactory, logger, workerFactory, identity, clock);

        // Act
        var act = async () => await pump.StopAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }
}
