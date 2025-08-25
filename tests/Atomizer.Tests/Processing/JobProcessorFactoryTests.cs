using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Processing;
using Microsoft.Extensions.Logging;

namespace Atomizer.Tests.Processing;

/// <summary>
/// Unit tests for <see cref="JobProcessorFactory"/>.
/// </summary>
public class JobProcessorFactoryTests
{
    private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();
    private readonly IAtomizerJobDispatcher _dispatcher = Substitute.For<IAtomizerJobDispatcher>();
    private readonly IAtomizerStorageScopeFactory _storageScopeFactory = Substitute.For<IAtomizerStorageScopeFactory>();
    private readonly ILoggerFactory _loggerFactory = Substitute.For<ILoggerFactory>();
    private readonly ILogger _logger = Substitute.For<ILogger>();

    private readonly JobProcessorFactory _sut;

    public JobProcessorFactoryTests()
    {
        _loggerFactory.CreateLogger(Arg.Any<string>()).Returns(_logger);
        _sut = new JobProcessorFactory(_clock, _dispatcher, _storageScopeFactory, _loggerFactory);
    }

    [Fact]
    public void Create_WhenCalledWithValidProcessorId_ShouldReturnJobProcessorWithCorrectDependencies()
    {
        // Arrange
        var workerId = new WorkerId("instance-1", QueueKey.Default, 0);

        // Act
        var processor = _sut.Create(workerId, Guid.NewGuid());

        // Assert
        processor.Should().NotBeNull();
        processor.Should().BeOfType<JobProcessor>();
    }

    [Fact]
    public void Create_WhenCalled_ShouldPassCorrectLoggerToJobProcessor()
    {
        // Arrange
        var workerId = new WorkerId("instance-1", QueueKey.Default, 0);
        var jobId = Guid.NewGuid();
        var processorId = $"{workerId}:*:{jobId}";

        // Act
        var processor = _sut.Create(workerId, jobId);

        // Assert
        processor.Should().NotBeNull();
        _loggerFactory.Received(1).CreateLogger(typeof(JobProcessor).FullName + ";" + processorId);
    }
}
