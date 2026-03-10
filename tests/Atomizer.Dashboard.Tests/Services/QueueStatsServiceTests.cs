using Atomizer.Abstractions;
using Atomizer.Dashboard.Services;

namespace Atomizer.Dashboard.Tests.Services;

/// <summary>
/// Unit tests for <see cref="QueueStatsService"/>.
/// </summary>
public class QueueStatsServiceTests
{
    private readonly IAtomizerDashboardStorage _storage = Substitute.For<IAtomizerDashboardStorage>();
    private readonly TestableLogger<QueueStatsService> _logger = Substitute.For<TestableLogger<QueueStatsService>>();
    private readonly QueueStatsService _sut;

    public QueueStatsServiceTests()
    {
        _sut = new QueueStatsService(_storage, _logger);
    }

    /// <summary>
    /// When no jobs exist the service should return an empty list without error.
    /// </summary>
    [Fact]
    public async Task GetStatsAsync_WhenNoJobsExist_ShouldReturnEmpty()
    {
        // Arrange
        _storage.GetQueueStatsAsync(CancellationToken.None).Returns(Array.Empty<QueueStats>());

        // Act
        var result = await _sut.GetStatsAsync(CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    /// <summary>
    /// When storage returns stats for a single queue the service should forward them
    /// without modification.
    /// </summary>
    [Fact]
    public async Task GetStatsAsync_WhenSingleQueueExists_ShouldReturnItsStats()
    {
        // Arrange
        var expected = new QueueStats(new QueueKey("default"), Pending: 3, Processing: 1, Completed: 10, Failed: 2);
        _storage.GetQueueStatsAsync(CancellationToken.None).Returns(new[] { expected });

        // Act
        var result = await _sut.GetStatsAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        var stat = result[0];
        stat.QueueKey.Key.Should().Be("default");
        stat.Pending.Should().Be(3);
        stat.Processing.Should().Be(1);
        stat.Completed.Should().Be(10);
        stat.Failed.Should().Be(2);
        stat.Total.Should().Be(16);
    }

    /// <summary>
    /// When multiple queues are returned by storage the service should forward all of them.
    /// </summary>
    [Fact]
    public async Task GetStatsAsync_WhenMultipleQueuesExist_ShouldReturnAllQueues()
    {
        // Arrange
        var stats = new[]
        {
            new QueueStats(new QueueKey("alpha"), 5, 0, 20, 1),
            new QueueStats(new QueueKey("beta"), 2, 1, 5, 0),
            new QueueStats(new QueueKey("gamma"), 0, 0, 100, 3),
        };
        _storage.GetQueueStatsAsync(CancellationToken.None).Returns(stats);

        // Act
        var result = await _sut.GetStatsAsync(CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Select(s => s.QueueKey.Key).Should().BeEquivalentTo("alpha", "beta", "gamma");
    }

    /// <summary>
    /// <see cref="QueueStats.Total"/> must equal the sum of all status counts.
    /// </summary>
    [Fact]
    public async Task GetStatsAsync_ShouldComputeTotalCorrectly()
    {
        // Arrange
        var stat = new QueueStats(new QueueKey("work"), 4, 2, 8, 1);
        _storage.GetQueueStatsAsync(CancellationToken.None).Returns(new[] { stat });

        // Act
        var result = await _sut.GetStatsAsync(CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        result[0].Total.Should().Be(15); // 4+2+8+1
    }

    /// <summary>
    /// When the storage call throws an unexpected exception the service should
    /// catch it, log an error, and return an empty list so the dashboard UI
    /// can render a graceful error state instead of crashing the Blazor circuit.
    /// </summary>
    [Fact]
    public async Task GetStatsAsync_WhenStorageThrows_ShouldLogErrorAndReturnEmpty()
    {
        // Arrange
        _storage
            .GetQueueStatsAsync(CancellationToken.None)
            .Returns<IReadOnlyList<QueueStats>>(_ => throw new InvalidOperationException("DB fail"));

        // Act
        var result = await _sut.GetStatsAsync(CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
        _logger.Received(1).LogError(Arg.Any<Exception>(), Arg.Any<string>());
    }

    /// <summary>
    /// When the operation is cancelled the service should re-throw
    /// <see cref="OperationCanceledException"/> so the caller can handle it.
    /// </summary>
    [Fact]
    public async Task GetStatsAsync_WhenCancelled_ShouldPropagateCancellation()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _storage
            .GetQueueStatsAsync(cts.Token)
            .Returns<IReadOnlyList<QueueStats>>(_ => throw new OperationCanceledException());

        // Act
        var act = () => _sut.GetStatsAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Verifies that <see cref="QueueStatsService.GetStatsAsync"/> delegates to
    /// <see cref="IAtomizerDashboardStorage.GetQueueStatsAsync"/> with the same
    /// cancellation token that was passed in.
    /// </summary>
    [Fact]
    public async Task GetStatsAsync_ShouldPassCancellationTokenToStorage()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        _storage.GetQueueStatsAsync(cts.Token).Returns(Array.Empty<QueueStats>());

        // Act
        await _sut.GetStatsAsync(cts.Token);

        // Assert
        await _storage.Received(1).GetQueueStatsAsync(cts.Token);
    }
}
