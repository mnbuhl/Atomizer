using System.Threading.Channels;
using Atomizer.Processing;

namespace Atomizer.Tests.Processing;

/// <summary>
/// Unit tests for <see cref="JobWorker"/>.
/// </summary>
public class JobWorkerTests
{
    private readonly JobWorker _sut;
    private readonly IJobProcessorFactory _jobProcessorFactory = Substitute.For<IJobProcessorFactory>();
    private readonly TestableLogger _logger = Substitute.For<TestableLogger>();

    private readonly CancellationTokenSource _ioCts = new();
    private readonly CancellationTokenSource _executionCts = new();

    public JobWorkerTests()
    {
        _sut = new JobWorker("worker-1", _jobProcessorFactory, _logger);
    }

    [Fact]
    public async Task RunAsync_WhenChannelIsClosed_ShouldExitGracefully()
    {
        // Arrange
    }
}
