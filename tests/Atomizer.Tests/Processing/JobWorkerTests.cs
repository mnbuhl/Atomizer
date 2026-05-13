using System.Text.Json;
using System.Threading.Channels;
using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Processing;
using Atomizer.Tests.Utilities.TestJobs;

namespace Atomizer.Tests.Processing;

/// <summary>
/// Unit tests for <see cref="JobWorker"/>.
/// </summary>
public class JobWorkerTests
{
    private readonly JobWorker _sut;
    private readonly IJobProcessorFactory _jobProcessorFactory = Substitute.For<IJobProcessorFactory>();
    private readonly IAtomizerServiceScopeFactory _scopeFactory = Substitute.For<IAtomizerServiceScopeFactory>();
    private readonly IAtomizerServiceScope _scope = Substitute.For<IAtomizerServiceScope>();
    private readonly IAtomizerStorage _storage = Substitute.For<IAtomizerStorage>();
    private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();
    private readonly TestableLogger _logger = Substitute.For<TestableLogger>();

    private readonly WorkerId _workerId = new("instance-1", QueueKey.Default, 0);
    private readonly AtomizerJob _job;
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    private static TimeSpan Timeout => TimeSpan.FromSeconds(2);

    public JobWorkerTests()
    {
        _scope.Storage.Returns(_storage);
        _scopeFactory.CreateScope().Returns(_scope);
        _clock.UtcNow.Returns(_now);

        _sut = new JobWorker(_workerId, _jobProcessorFactory, _scopeFactory, _clock, _logger);

        _job = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            JsonSerializer.Serialize(new WriteLineMessage("Hello, World!")),
            _now,
            _now
        );
    }

    [Fact]
    public async Task RunAsync_WhenSingleJobProcessed_ShouldCreateProcessorAndProcessJob()
    {
        // Arrange
        var ioCts = new CancellationTokenSource();
        var executionCts = new CancellationTokenSource();

        var channel = Channel.CreateUnbounded<JobBatch>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
        );

        var job = _job;
        channel.Writer.TryWrite(new JobBatch(new[] { job }));

        var processor = Substitute.For<IJobProcessor>();
        var processedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        processor
            .ProcessAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                processedTcs.TrySetResult(true);
                return Task.CompletedTask;
            });

        _jobProcessorFactory.Create(Arg.Any<WorkerId>(), Arg.Any<Guid>()).Returns(processor);

        // Act
        var runTask = _sut.RunAsync(channel.Reader, ioCts.Token, executionCts.Token);
        var processed = await WaitOrTimeout(processedTcs.Task, Timeout);

        // Assert
        processed.Should().BeTrue("processor should be invoked for the enqueued job");

        ioCts.Cancel();
        (await WaitOrTimeout(runTask, Timeout)).Should().BeTrue("worker should exit after IO cancellation");

        await processor.Received(1).ProcessAsync(job, executionCts.Token);
        _jobProcessorFactory.Received(1).Create(_workerId, job.Id);

        _logger.Received(1).LogDebug($"Worker {_workerId} started");
        _logger.Received(1).LogDebug($"Worker {_workerId} cancellation requested");
        _logger.Received(1).LogDebug($"Worker {_workerId} stopped");
    }

    [Fact]
    public async Task RunAsync_WhenIoTokenAlreadyCanceled_ShouldStartThenStopWithoutReading()
    {
        // Arrange
        var ioCts = new CancellationTokenSource();
        var executionCts = new CancellationTokenSource();

        // Ensure IO token is already canceled before starting
        ioCts.Cancel();

        var channel = Channel.CreateUnbounded<JobBatch>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
        );

        // Act
        var run = _sut.RunAsync(channel.Reader, ioCts.Token, executionCts.Token);
        (await WaitOrTimeout(run, Timeout))
            .Should()
            .BeTrue("worker should stop immediately when IO token is already canceled");

        // Assert
        _jobProcessorFactory.DidNotReceiveWithAnyArgs().Create(null!, Guid.Empty);
        _logger.Received(1).LogDebug($"Worker {_workerId} started");
        _logger.DidNotReceive().LogWarning($"Worker {_workerId} cancellation requested");
        _logger.Received(1).LogDebug($"Worker {_workerId} stopped");
    }

    [Fact]
    public async Task RunAsync_WhenIoCancellationDuringRead_ShouldLogAndStop()
    {
        // Arrange
        var ioCts = new CancellationTokenSource();
        var executionCts = new CancellationTokenSource();

        // No items written; ReadAsync will pend until IO token is canceled.
        var channel = Channel.CreateUnbounded<JobBatch>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
        );

        // Act
        var run = _sut.RunAsync(channel.Reader, ioCts.Token, executionCts.Token);

        ioCts.Cancel(); // trigger OperationCanceledException in ReadAsync
        (await WaitOrTimeout(run, Timeout)).Should().BeTrue("worker should stop on IO cancellation during read");

        // Assert
        _logger.Received(1).LogDebug($"Worker {_workerId} started");
        _logger.Received(1).LogDebug($"Worker {_workerId} cancellation requested");
        _logger.Received(1).LogDebug($"Worker {_workerId} stopped");
    }

    [Fact]
    public async Task RunAsync_WhenReaderThrowsUnexpected_ShouldLogAndContinueToNextJob()
    {
        // Arrange
        var ioCts = new CancellationTokenSource();
        var executionCts = new CancellationTokenSource();

        var maxReadAttempts = NonPublicSpy.GetConstant<JobWorker, int>("MaxReadAttempts");
        var nextJob = _job; // the job that should be processed after the read failures are skipped
        var reader = new ThrowThenReturnReader(nextJob, maxReadAttempts);

        var processor = Substitute.For<IJobProcessor>();
        var processedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        processor
            .ProcessAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                processedTcs.TrySetResult(true);
                return Task.CompletedTask;
            });

        _jobProcessorFactory.Create(Arg.Any<WorkerId>(), Arg.Any<Guid>()).Returns(processor);

        // Act
        var run = Task.Run(
            () => _sut.RunAsync(reader, ioCts.Token, executionCts.Token),
            TestContext.Current.CancellationToken
        );

        (await WaitOrTimeout(processedTcs.Task, Timeout))
            .Should()
            .BeTrue("worker should continue after a read failure and process the next job");

        ioCts.Cancel(); // exit loop cleanly
        (await WaitOrTimeout(run, Timeout)).Should().BeTrue();

        // Assert
        _logger
            .Received(maxReadAttempts)
            .LogWarning(Arg.Any<Exception>(), $"Worker {_workerId} channel read operation failed");
        await processor.Received(1).ProcessAsync(nextJob, executionCts.Token);
        _logger.Received(1).LogDebug($"Worker {_workerId} stopped");
    }

    [Fact]
    public async Task RunAsync_WhenExecutionCanceled_ShouldLogDebugAndStop()
    {
        // Arrange
        var ioCts = new CancellationTokenSource();
        var executionCts = new CancellationTokenSource();

        var channel = Channel.CreateUnbounded<JobBatch>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
        );
        channel.Writer.TryWrite(new JobBatch(new[] { _job }));

        var processor = Substitute.For<IJobProcessor>();
        processor
            .ProcessAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                executionCts.Cancel(); // ensure catch filter matches execution token
                throw new OperationCanceledException();
            });

        _jobProcessorFactory.Create(Arg.Any<WorkerId>(), Arg.Any<Guid>()).Returns(processor);

        // Act
        var run = _sut.RunAsync(channel.Reader, ioCts.Token, executionCts.Token);
        (await WaitOrTimeout(run, Timeout)).Should().BeTrue("worker should stop on execution cancellation");

        // Assert
        _logger.Received(1).LogDebug($"Worker {_workerId} started");
        _logger.Received(1).LogDebug($"Worker {_workerId} cancellation requested");
        _logger.Received(1).LogDebug($"Worker {_workerId} stopped");
    }

    [Fact]
    public async Task RunAsync_WhenProcessorThrows_ShouldLogErrorAndContinueToNextJob()
    {
        // Arrange
        var ioCts = new CancellationTokenSource();
        var executionCts = new CancellationTokenSource();

        var channel = Channel.CreateUnbounded<JobBatch>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
        );

        var first = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            JsonSerializer.Serialize(new WriteLineMessage("boom 1")),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow
        );

        var second = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            JsonSerializer.Serialize(new WriteLineMessage("ok 2")),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow
        );

        channel.Writer.TryWrite(new JobBatch(new[] { first }));
        channel.Writer.TryWrite(new JobBatch(new[] { second }));

        var processor = Substitute.For<IJobProcessor>();
        var invocation = 0;
        var secondProcessed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        processor
            .ProcessAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                invocation++;
                if (invocation == 1)
                {
                    throw new InvalidOperationException("boom");
                }

                secondProcessed.TrySetResult(true);
                return Task.CompletedTask;
            });

        _jobProcessorFactory.Create(Arg.Any<WorkerId>(), Arg.Any<Guid>()).Returns(processor);

        // Act
        var run = _sut.RunAsync(channel.Reader, ioCts.Token, executionCts.Token);

        (await WaitOrTimeout(secondProcessed.Task, Timeout))
            .Should()
            .BeTrue("worker should continue after processor failure and handle next job");

        ioCts.Cancel(); // end loop
        (await WaitOrTimeout(run, Timeout)).Should().BeTrue();

        // Assert
        _logger
            .Received()
            .LogError(
                Arg.Is<InvalidOperationException>(ex => ex.Message == "boom"),
                $"Worker {_workerId} failed to process job {first.Id}"
            );

        await processor.Received(2).ProcessAsync(Arg.Any<AtomizerJob>(), executionCts.Token);
        _logger.Received(1).LogDebug($"Worker {_workerId} stopped");
    }

    [Fact]
    public async Task RunAsync_WhenPartitionBatchHeadRetries_ShouldReleaseRemainingJobsWithoutProcessingThem()
    {
        // Arrange
        var ioCts = new CancellationTokenSource();
        var executionCts = new CancellationTokenSource();

        var channel = Channel.CreateUnbounded<JobBatch>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
        );

        var partitionKey = new PartitionKey("customer-1");
        var leaseToken = new LeaseToken("instance-1:*:default:*:lease");
        var first = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            JsonSerializer.Serialize(new WriteLineMessage("first")),
            _now,
            _now,
            partitionKey: partitionKey
        );
        var second = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            JsonSerializer.Serialize(new WriteLineMessage("second")),
            _now,
            _now,
            partitionKey: partitionKey
        );

        first.SequenceNumber = 1;
        second.SequenceNumber = 2;
        first.Lease(leaseToken, _now, TimeSpan.FromMinutes(10));
        second.Lease(leaseToken, _now, TimeSpan.FromMinutes(10));
        channel.Writer.TryWrite(new JobBatch(new[] { first, second }));

        var firstProcessor = Substitute.For<IJobProcessor>();
        var secondProcessor = Substitute.For<IJobProcessor>();
        var firstProcessed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReleased = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        firstProcessor
            .ProcessAsync(first, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                first.Attempt();
                first.Reschedule(_now.AddMinutes(1), _now);
                firstProcessed.TrySetResult(true);
                return Task.CompletedTask;
            });

        _storage
            .UpdateJobsAsync(Arg.Any<IEnumerable<AtomizerJob>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var jobs = call.ArgAt<IEnumerable<AtomizerJob>>(0).ToList();
                if (jobs.Count == 1 && jobs[0].Id == second.Id && jobs[0].Status == AtomizerJobStatus.Pending)
                {
                    secondReleased.TrySetResult(true);
                }

                return Task.CompletedTask;
            });

        _jobProcessorFactory.Create(_workerId, first.Id).Returns(firstProcessor);
        _jobProcessorFactory.Create(_workerId, second.Id).Returns(secondProcessor);

        // Act
        var runTask = _sut.RunAsync(channel.Reader, ioCts.Token, executionCts.Token);

        (await WaitOrTimeout(firstProcessed.Task, Timeout)).Should().BeTrue();
        (await WaitOrTimeout(secondReleased.Task, Timeout)).Should().BeTrue();

        ioCts.Cancel();
        (await WaitOrTimeout(runTask, Timeout)).Should().BeTrue();

        // Assert
        await firstProcessor.Received(1).ProcessAsync(first, executionCts.Token);
        await secondProcessor.DidNotReceiveWithAnyArgs().ProcessAsync(null!, CancellationToken.None);
        second.Status.Should().Be(AtomizerJobStatus.Pending);
        second.LeaseToken.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_WhenPartitionBatchSucceeds_ShouldProcessJobsInSequenceWithoutReleasingRemainingJobs()
    {
        // Arrange
        var ioCts = new CancellationTokenSource();
        var executionCts = new CancellationTokenSource();
        var channel = CreateChannel();

        var jobs = new[]
        {
            CreatePartitionedJob("customer-1", 1, "first"),
            CreatePartitionedJob("customer-1", 2, "second"),
            CreatePartitionedJob("customer-1", 3, "third"),
        };

        foreach (var job in jobs)
        {
            LeaseJob(job);
        }

        channel.Writer.TryWrite(new JobBatch(jobs));

        var processedIds = new List<Guid>();
        var processedAll = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = Substitute.For<IJobProcessor>();

        processor
            .ProcessAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var job = call.ArgAt<AtomizerJob>(0);
                processedIds.Add(job.Id);
                job.MarkAsCompleted(_now);

                if (processedIds.Count == jobs.Length)
                {
                    processedAll.TrySetResult(true);
                }

                return Task.CompletedTask;
            });

        _jobProcessorFactory.Create(Arg.Any<WorkerId>(), Arg.Any<Guid>()).Returns(processor);

        // Act
        var runTask = _sut.RunAsync(channel.Reader, ioCts.Token, executionCts.Token);
        (await WaitOrTimeout(processedAll.Task, Timeout)).Should().BeTrue();

        ioCts.Cancel();
        (await WaitOrTimeout(runTask, Timeout)).Should().BeTrue();

        // Assert
        processedIds.Should().ContainInOrder(jobs.Select(j => j.Id));
        _ = _storage.DidNotReceive().UpdateJobsAsync(Arg.Any<IEnumerable<AtomizerJob>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WhenPartitionBatchFirstJobIsStillRunning_ShouldNotStartSecondJob()
    {
        // Arrange
        var ioCts = new CancellationTokenSource();
        var executionCts = new CancellationTokenSource();
        var channel = CreateChannel();

        var first = CreatePartitionedJob("customer-1", 1, "first");
        var second = CreatePartitionedJob("customer-1", 2, "second");
        LeaseJob(first);
        LeaseJob(second);
        channel.Writer.TryWrite(new JobBatch(new[] { first, second }));

        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstProcessor = Substitute.For<IJobProcessor>();
        firstProcessor
            .ProcessAsync(first, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                firstStarted.TrySetResult(true);
                await releaseFirst.Task;
                first.MarkAsCompleted(_now);
            });

        var secondProcessor = Substitute.For<IJobProcessor>();
        secondProcessor
            .ProcessAsync(second, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                second.MarkAsCompleted(_now);
                secondStarted.TrySetResult(true);
                return Task.CompletedTask;
            });

        _jobProcessorFactory.Create(_workerId, first.Id).Returns(firstProcessor);
        _jobProcessorFactory.Create(_workerId, second.Id).Returns(secondProcessor);

        // Act
        var runTask = _sut.RunAsync(channel.Reader, ioCts.Token, executionCts.Token);

        (await WaitOrTimeout(firstStarted.Task, Timeout)).Should().BeTrue();
        (await WaitOrTimeout(secondStarted.Task, TimeSpan.FromMilliseconds(150)))
            .Should()
            .BeFalse("jobs in the same partition batch must not run concurrently");

        releaseFirst.TrySetResult(true);
        (await WaitOrTimeout(secondStarted.Task, Timeout)).Should().BeTrue();

        ioCts.Cancel();
        (await WaitOrTimeout(runTask, Timeout)).Should().BeTrue();

        // Assert
        await firstProcessor.Received(1).ProcessAsync(first, executionCts.Token);
        await secondProcessor.Received(1).ProcessAsync(second, executionCts.Token);
    }

    [Fact]
    public async Task RunAsync_WhenPartitionBatchMiddleJobRetries_ShouldReleaseOnlyJobsAfterRetry()
    {
        // Arrange
        var ioCts = new CancellationTokenSource();
        var executionCts = new CancellationTokenSource();
        var channel = CreateChannel();

        var first = CreatePartitionedJob("customer-1", 1, "first");
        var second = CreatePartitionedJob("customer-1", 2, "second");
        var third = CreatePartitionedJob("customer-1", 3, "third");
        LeaseJob(first);
        LeaseJob(second);
        LeaseJob(third);
        channel.Writer.TryWrite(new JobBatch(new[] { first, second, third }));

        var releasedJobs = new TaskCompletionSource<IReadOnlyList<AtomizerJob>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        _storage
            .UpdateJobsAsync(Arg.Any<IEnumerable<AtomizerJob>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                releasedJobs.TrySetResult(call.ArgAt<IEnumerable<AtomizerJob>>(0).ToList());
                return Task.CompletedTask;
            });

        var firstProcessor = Substitute.For<IJobProcessor>();
        firstProcessor
            .ProcessAsync(first, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                first.MarkAsCompleted(_now);
                return Task.CompletedTask;
            });

        var secondProcessor = Substitute.For<IJobProcessor>();
        secondProcessor
            .ProcessAsync(second, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                second.Attempt();
                second.Reschedule(_now.AddMinutes(1), _now);
                return Task.CompletedTask;
            });

        var thirdProcessor = Substitute.For<IJobProcessor>();

        _jobProcessorFactory.Create(_workerId, first.Id).Returns(firstProcessor);
        _jobProcessorFactory.Create(_workerId, second.Id).Returns(secondProcessor);
        _jobProcessorFactory.Create(_workerId, third.Id).Returns(thirdProcessor);

        // Act
        var runTask = _sut.RunAsync(channel.Reader, ioCts.Token, executionCts.Token);
        var released = await WaitForResultOrTimeout(releasedJobs.Task, Timeout);

        ioCts.Cancel();
        (await WaitOrTimeout(runTask, Timeout)).Should().BeTrue();

        // Assert
        released.Should().ContainSingle().Which.Id.Should().Be(third.Id);
        first.Status.Should().Be(AtomizerJobStatus.Completed);
        second.Status.Should().Be(AtomizerJobStatus.Pending);
        second.Attempts.Should().Be(1);
        third.Status.Should().Be(AtomizerJobStatus.Pending);
        third.LeaseToken.Should().BeNull();

        await thirdProcessor.DidNotReceiveWithAnyArgs().ProcessAsync(null!, CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_WhenPartitionBatchProcessorThrows_ShouldReleaseRemainingJobsWithoutProcessingThem()
    {
        // Arrange
        var ioCts = new CancellationTokenSource();
        var executionCts = new CancellationTokenSource();
        var channel = CreateChannel();

        var first = CreatePartitionedJob("customer-1", 1, "first");
        var second = CreatePartitionedJob("customer-1", 2, "second");
        var third = CreatePartitionedJob("customer-1", 3, "third");
        LeaseJob(first);
        LeaseJob(second);
        LeaseJob(third);
        channel.Writer.TryWrite(new JobBatch(new[] { first, second, third }));

        var releasedJobs = new TaskCompletionSource<IReadOnlyList<AtomizerJob>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        _storage
            .UpdateJobsAsync(Arg.Any<IEnumerable<AtomizerJob>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                releasedJobs.TrySetResult(call.ArgAt<IEnumerable<AtomizerJob>>(0).ToList());
                return Task.CompletedTask;
            });

        var firstProcessor = Substitute.For<IJobProcessor>();
        firstProcessor
            .ProcessAsync(first, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                first.MarkAsCompleted(_now);
                return Task.CompletedTask;
            });

        var secondProcessor = Substitute.For<IJobProcessor>();
        secondProcessor
            .ProcessAsync(second, Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("boom"));

        var thirdProcessor = Substitute.For<IJobProcessor>();

        _jobProcessorFactory.Create(_workerId, first.Id).Returns(firstProcessor);
        _jobProcessorFactory.Create(_workerId, second.Id).Returns(secondProcessor);
        _jobProcessorFactory.Create(_workerId, third.Id).Returns(thirdProcessor);

        // Act
        var runTask = _sut.RunAsync(channel.Reader, ioCts.Token, executionCts.Token);
        var released = await WaitForResultOrTimeout(releasedJobs.Task, Timeout);

        ioCts.Cancel();
        (await WaitOrTimeout(runTask, Timeout)).Should().BeTrue();

        // Assert
        released.Should().ContainSingle().Which.Id.Should().Be(third.Id);
        first.Status.Should().Be(AtomizerJobStatus.Completed);
        second.Status.Should().Be(AtomizerJobStatus.Processing);
        third.Status.Should().Be(AtomizerJobStatus.Pending);
        third.LeaseToken.Should().BeNull();

        await thirdProcessor.DidNotReceiveWithAnyArgs().ProcessAsync(null!, CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_WhenExecutionCanceledDuringPartitionBatch_ShouldReleaseRemainingJobsAndStop()
    {
        // Arrange
        var ioCts = new CancellationTokenSource();
        var executionCts = new CancellationTokenSource();
        var channel = CreateChannel();

        var first = CreatePartitionedJob("customer-1", 1, "first");
        var second = CreatePartitionedJob("customer-1", 2, "second");
        var third = CreatePartitionedJob("customer-1", 3, "third");
        LeaseJob(first);
        LeaseJob(second);
        LeaseJob(third);
        channel.Writer.TryWrite(new JobBatch(new[] { first, second, third }));

        var releasedJobs = new TaskCompletionSource<IReadOnlyList<AtomizerJob>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        _storage
            .UpdateJobsAsync(Arg.Any<IEnumerable<AtomizerJob>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                releasedJobs.TrySetResult(call.ArgAt<IEnumerable<AtomizerJob>>(0).ToList());
                return Task.CompletedTask;
            });

        var firstProcessor = Substitute.For<IJobProcessor>();
        firstProcessor
            .ProcessAsync(first, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                executionCts.Cancel();
                throw new OperationCanceledException(executionCts.Token);
            });

        var secondProcessor = Substitute.For<IJobProcessor>();
        var thirdProcessor = Substitute.For<IJobProcessor>();

        _jobProcessorFactory.Create(_workerId, first.Id).Returns(firstProcessor);
        _jobProcessorFactory.Create(_workerId, second.Id).Returns(secondProcessor);
        _jobProcessorFactory.Create(_workerId, third.Id).Returns(thirdProcessor);

        // Act
        var runTask = _sut.RunAsync(channel.Reader, ioCts.Token, executionCts.Token);
        var released = await WaitForResultOrTimeout(releasedJobs.Task, Timeout);

        // Assert
        (await WaitOrTimeout(runTask, Timeout))
            .Should()
            .BeTrue();
        released.Select(j => j.Id).Should().ContainInOrder(second.Id, third.Id);
        second.Status.Should().Be(AtomizerJobStatus.Pending);
        third.Status.Should().Be(AtomizerJobStatus.Pending);

        await secondProcessor.DidNotReceiveWithAnyArgs().ProcessAsync(null!, CancellationToken.None);
        await thirdProcessor.DidNotReceiveWithAnyArgs().ProcessAsync(null!, CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_WhenTwoWorkersReadUnpartitionedBatches_ShouldProcessJobsConcurrently()
    {
        // Arrange
        var ioCts = new CancellationTokenSource();
        var executionCts = new CancellationTokenSource();
        var channel = Channel.CreateUnbounded<JobBatch>();
        var secondWorkerId = new WorkerId("instance-1", QueueKey.Default, 1);
        var secondWorker = new JobWorker(secondWorkerId, _jobProcessorFactory, _scopeFactory, _clock, _logger);

        var first = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            JsonSerializer.Serialize(new WriteLineMessage("first")),
            _now,
            _now
        );
        var second = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            JsonSerializer.Serialize(new WriteLineMessage("second")),
            _now,
            _now
        );
        LeaseJob(first);
        LeaseJob(second);

        channel.Writer.TryWrite(new JobBatch(new[] { first }));
        channel.Writer.TryWrite(new JobBatch(new[] { second }));

        var releaseProcessors = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstProcessor = CreateBlockingProcessor(first, firstStarted, releaseProcessors);
        var secondProcessor = CreateBlockingProcessor(second, secondStarted, releaseProcessors);

        _jobProcessorFactory.Create(Arg.Any<WorkerId>(), first.Id).Returns(firstProcessor);
        _jobProcessorFactory.Create(Arg.Any<WorkerId>(), second.Id).Returns(secondProcessor);

        // Act
        var firstRun = _sut.RunAsync(channel.Reader, ioCts.Token, executionCts.Token);
        var secondRun = secondWorker.RunAsync(channel.Reader, ioCts.Token, executionCts.Token);

        (await WaitOrTimeout(firstStarted.Task, Timeout)).Should().BeTrue();
        (await WaitOrTimeout(secondStarted.Task, Timeout))
            .Should()
            .BeTrue("separate unpartitioned batches should remain parallel across workers");

        releaseProcessors.TrySetResult(true);
        ioCts.Cancel();

        // Assert
        (await WaitOrTimeout(Task.WhenAll(firstRun, secondRun), Timeout))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task RunAsync_WhenTwoWorkersReadDifferentPartitionBatches_ShouldProcessPartitionsConcurrently()
    {
        // Arrange
        var ioCts = new CancellationTokenSource();
        var executionCts = new CancellationTokenSource();
        var channel = Channel.CreateUnbounded<JobBatch>();
        var secondWorkerId = new WorkerId("instance-1", QueueKey.Default, 1);
        var secondWorker = new JobWorker(secondWorkerId, _jobProcessorFactory, _scopeFactory, _clock, _logger);

        var first = CreatePartitionedJob("customer-1", 1, "first");
        var second = CreatePartitionedJob("customer-2", 1, "second");
        LeaseJob(first);
        LeaseJob(second);

        channel.Writer.TryWrite(new JobBatch(new[] { first }));
        channel.Writer.TryWrite(new JobBatch(new[] { second }));

        var releaseProcessors = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstProcessor = CreateBlockingProcessor(first, firstStarted, releaseProcessors);
        var secondProcessor = CreateBlockingProcessor(second, secondStarted, releaseProcessors);

        _jobProcessorFactory.Create(Arg.Any<WorkerId>(), first.Id).Returns(firstProcessor);
        _jobProcessorFactory.Create(Arg.Any<WorkerId>(), second.Id).Returns(secondProcessor);

        // Act
        var firstRun = _sut.RunAsync(channel.Reader, ioCts.Token, executionCts.Token);
        var secondRun = secondWorker.RunAsync(channel.Reader, ioCts.Token, executionCts.Token);

        (await WaitOrTimeout(firstStarted.Task, Timeout)).Should().BeTrue();
        (await WaitOrTimeout(secondStarted.Task, Timeout))
            .Should()
            .BeTrue("different partition batches should remain parallel across workers");

        releaseProcessors.TrySetResult(true);
        ioCts.Cancel();

        // Assert
        (await WaitOrTimeout(Task.WhenAll(firstRun, secondRun), Timeout))
            .Should()
            .BeTrue();
    }

    private static async Task<bool> WaitOrTimeout(Task task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        return completed == task && task.IsCompleted;
    }

    private static async Task<T> WaitForResultOrTimeout<T>(Task<T> task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        completed.Should().Be(task);
        return await task;
    }

    private Channel<JobBatch> CreateChannel()
    {
        return Channel.CreateUnbounded<JobBatch>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
        );
    }

    private AtomizerJob CreatePartitionedJob(string partitionKey, long sequenceNumber, string message)
    {
        var job = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            JsonSerializer.Serialize(new WriteLineMessage(message)),
            _now,
            _now,
            partitionKey: new PartitionKey(partitionKey)
        );

        job.SequenceNumber = sequenceNumber;
        return job;
    }

    private void LeaseJob(AtomizerJob job)
    {
        job.Lease(new LeaseToken("instance-1:*:default:*:lease"), _now, TimeSpan.FromMinutes(10));
    }

    private IJobProcessor CreateBlockingProcessor(
        AtomizerJob job,
        TaskCompletionSource<bool> started,
        TaskCompletionSource<bool> release
    )
    {
        var processor = Substitute.For<IJobProcessor>();
        processor
            .ProcessAsync(job, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                started.TrySetResult(true);
                await release.Task;
                job.MarkAsCompleted(_now);
            });

        return processor;
    }

    private sealed class ThrowThenReturnReader : ChannelReader<JobBatch>
    {
        private readonly JobBatch _next;
        private readonly int _failuresBeforeJob;
        private int _readAttempts;
        private int _returned;

        public ThrowThenReturnReader(AtomizerJob next, int failuresBeforeJob)
        {
            _next = new JobBatch(new[] { next });
            _failuresBeforeJob = failuresBeforeJob;
        }

        public override async ValueTask<JobBatch> ReadAsync(CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref _readAttempts);

            if (attempt <= _failuresBeforeJob)
            {
                throw new InvalidOperationException("unexpected read error");
            }

            if (Interlocked.Exchange(ref _returned, 1) == 0)
            {
                return _next;
            }

            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
            throw new OperationCanceledException(cancellationToken);
        }

        public override bool TryRead(out JobBatch item)
        {
            item = default!;
            return false;
        }

        public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(true);
        }
    }
}
