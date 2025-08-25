using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Processing;

namespace Atomizer.Tests.Processing;

/// <summary>
/// Unit tests for <see cref="JobProcessor"/>.
/// </summary>
public class JobProcessorTests
{
    private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();
    private readonly IAtomizerJobDispatcher _dispatcher = Substitute.For<IAtomizerJobDispatcher>();
    private readonly IAtomizerStorageScopeFactory _storageScopeFactory = Substitute.For<IAtomizerStorageScopeFactory>();
    private readonly IAtomizerStorageScope _storageScope = Substitute.For<IAtomizerStorageScope>();
    private readonly IAtomizerStorage _storage = Substitute.For<IAtomizerStorage>();
    private readonly TestableLogger _logger = Substitute.For<TestableLogger>();
    private readonly JobProcessor _sut;
    private readonly AtomizerJob _job;
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    public JobProcessorTests()
    {
        _clock.UtcNow.Returns(_now);
        _storageScope.Storage.Returns(_storage);
        _storageScopeFactory.CreateScope().Returns(_storageScope);
        _sut = new JobProcessor(_clock, _dispatcher, _storageScopeFactory, _logger);
        _job = AtomizerJob.Create(QueueKey.Default, typeof(string), "payload", _now, _now);
        _job.Status = AtomizerJobStatus.Processing;
    }

    [Fact]
    public async Task ProcessAsync_WhenJobSucceeds_ShouldMarkCompletedAndUpdateStorageAndLog()
    {
        // Arrange
        // Act
        await _sut.ProcessAsync(_job, CancellationToken.None);

        // Assert
        _job.Status.Should().Be(AtomizerJobStatus.Completed);
        _job.Attempts.Should().Be(1);
        _job.CompletedAt.Should().Be(_now);
        _job.UpdatedAt.Should().Be(_now);
        _job.LeaseToken.Should().BeNull();
        _job.VisibleAt.Should().BeNull();
        _job.Errors.Should().BeEmpty();

        await _storage.Received(1).UpdateAsync(_job, Arg.Any<CancellationToken>());

        _logger.Received().LogDebug($"Executing job {_job.Id} (attempt {_job.Attempts}) on '{_job.QueueKey}'");
        _logger.Received().LogInformation(Arg.Is<string>(s => s.StartsWith($"Job {_job.Id} succeeded in")));
    }

    [Fact]
    public async Task ProcessAsync_WhenOperationCanceled_ShouldLogWarning()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();
        _dispatcher
            .DispatchAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new OperationCanceledException());

        // Act
        await _sut.ProcessAsync(_job, cts.Token);

        // Assert
        _logger.Received().LogWarning($"Operation cancelled while processing job {_job.Id} on '{_job.QueueKey}'");
    }

    [Fact]
    public async Task ProcessAsync_WhenDispatcherThrows_ShouldHandleFailureAndLog()
    {
        // Arrange
        var ex = new InvalidOperationException("boom");
        _dispatcher.DispatchAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>()).Returns(_ => throw ex);

        // Act
        await _sut.ProcessAsync(_job, CancellationToken.None);

        // Assert
        _job.Status.Should().Be(AtomizerJobStatus.Pending);
        _job.Attempts.Should().Be(1);
        _job.UpdatedAt.Should().Be(_now);
        _job.VisibleAt.Should().NotBeNull();

        _job.Errors.Should().ContainSingle();
        await _storage.Received(1).UpdateAsync(_job, Arg.Any<CancellationToken>());
        _logger
            .Received()
            .LogWarning(
                Arg.Is<string>(s =>
                    s.StartsWith(
                        $"Job {_job.Id} failed (attempt {_job.Attempts}) on '{_job.QueueKey}', retrying after "
                    )
                )
            );
    }

    [Fact]
    public async Task HandleFailureAsync_WhenShouldNotRetry_ShouldMarkFailedAndLogError()
    {
        // Arrange
        var ex = new Exception("fail");
        _dispatcher.DispatchAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>()).Returns(_ => throw ex);
        _job.Attempts = _job.MaxAttempts - 1;

        // Act
        await _sut.ProcessAsync(_job, CancellationToken.None);

        // Assert
        _job.Status.Should().Be(AtomizerJobStatus.Failed);
        _job.Attempts.Should().Be(_job.MaxAttempts);
        _job.FailedAt.Should().Be(_now);
        _job.UpdatedAt.Should().Be(_now);
        _job.LeaseToken.Should().BeNull();
        _job.VisibleAt.Should().BeNull();
        _job.Errors.Should().ContainSingle();

        _logger.Received().LogError($"Job {_job.Id} exhausted retries and was marked as failed on '{_job.QueueKey}'");
    }

    [Fact]
    public async Task HandleFailureAsync_WhenStorageUpdateThrows_ShouldLogError()
    {
        // Arrange
        _storage
            .UpdateAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new Exception("storage fail"));

        // Act
        await _sut.ProcessAsync(_job, CancellationToken.None);

        // Assert
        _logger
            .Received()
            .LogError(
                Arg.Any<Exception>(),
                $"Error while handling failure of job {_job.Id} on '{_job.QueueKey}' on attempt {_job.Attempts}"
            );
    }
}
