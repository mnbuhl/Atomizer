using System.Text.Json;
using System.Threading.Channels;
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
    private readonly TestableLogger _logger = Substitute.For<TestableLogger>();

    private readonly WorkerId _workerId = new("instance-1", QueueKey.Default, 0);
    private readonly AtomizerJob _job;

    private static TimeSpan Timeout => TimeSpan.FromSeconds(2);

    public JobWorkerTests()
    {
        _sut = new JobWorker(_workerId, _jobProcessorFactory, _logger);

        var now = DateTimeOffset.UtcNow;
        _job = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            JsonSerializer.Serialize(new WriteLineMessage("Hello, World!")),
            now,
            now
        );
    }

    [Fact]
    public async Task RunAsync_WhenSingleJobProcessed_ShouldCreateProcessorAndProcessJob()
    {
        // Arrange
        var ioCts = new CancellationTokenSource();
        var executionCts = new CancellationTokenSource();

        var channel = Channel.CreateUnbounded<AtomizerJob>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
        );

        var job = _job;
        channel.Writer.TryWrite(job);

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

        var channel = Channel.CreateUnbounded<AtomizerJob>(
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
        var channel = Channel.CreateUnbounded<AtomizerJob>(
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

        var nextJob = _job; // the job that should be processed after the read failure
        var reader = new ThrowThenReturnReader(nextJob);

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
        var maxReadAttempts = NonPublicSpy.GetConstant<JobWorker, int>("MaxReadAttempts");
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

        var channel = Channel.CreateUnbounded<AtomizerJob>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
        );
        channel.Writer.TryWrite(_job);

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

        var channel = Channel.CreateUnbounded<AtomizerJob>(
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

        channel.Writer.TryWrite(first);
        channel.Writer.TryWrite(second);

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

    private static async Task<bool> WaitOrTimeout(Task task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        return completed == task && task.IsCompleted;
    }

    private sealed class ThrowThenReturnReader : ChannelReader<AtomizerJob>
    {
        private readonly AtomizerJob _next;
        private int _reads;

        private readonly List<object> _items = new();

        public ThrowThenReturnReader(AtomizerJob next)
        {
            _next = next;

            _items.Add(new Exception("unexpected read error"));
            _items.Add(next);
        }

        public override async ValueTask<AtomizerJob> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(50, cancellationToken); // simulate some delay

            return _items[_reads] switch
            {
                Exception ex => throw ex,
                _ => _next,
            };
        }

        public override bool TryRead(out AtomizerJob item)
        {
            Interlocked.Increment(ref _reads);
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
