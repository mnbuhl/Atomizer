using Atomizer.Dashboard.Abstractions;
using Atomizer.Dashboard.Services;

namespace Atomizer.Dashboard.Tests.Services;

/// <summary>
/// Unit tests for <see cref="CancelJobService"/>.
/// </summary>
public class CancelJobServiceTests
{
    private readonly IDashboardStorage _storage = Substitute.For<IDashboardStorage>();
    private readonly TestableLogger<CancelJobService> _logger = Substitute.For<
        TestableLogger<CancelJobService>
    >();
    private readonly CancelJobService _sut;
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    public CancelJobServiceTests()
    {
        _sut = new CancelJobService(_storage, _logger);
    }

    /// <summary>
    /// When the storage layer returns <see langword="true"/> the service should
    /// propagate that result and log an informational message.
    /// </summary>
    [Fact]
    public async Task CancelAsync_WhenStorageReturnTrue_ShouldReturnTrueAndLogInfo()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        _storage.CancelJobAsync(jobId, CancellationToken.None).Returns(true);

        // Act
        var result = await _sut.CancelAsync(jobId, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        _logger.Received(1).LogInformation(Arg.Any<string>());
    }

    /// <summary>
    /// When the storage layer returns <see langword="false"/> (job not found or not
    /// pending) the service should propagate that result and log a warning.
    /// </summary>
    [Fact]
    public async Task CancelAsync_WhenStorageReturnFalse_ShouldReturnFalseAndLogWarning()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        _storage.CancelJobAsync(jobId, CancellationToken.None).Returns(false);

        // Act
        var result = await _sut.CancelAsync(jobId, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        _logger.Received(1).LogWarning(Arg.Any<string>());
    }

    /// <summary>
    /// When the storage call throws an unexpected exception the service should
    /// catch it, log an error, and return <see langword="false"/> so the
    /// Blazor component can display a graceful error state.
    /// </summary>
    [Fact]
    public async Task CancelAsync_WhenStorageThrows_ShouldLogErrorAndReturnFalse()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        _storage
            .CancelJobAsync(jobId, CancellationToken.None)
            .Returns<bool>(_ => throw new InvalidOperationException("DB error"));

        // Act
        var result = await _sut.CancelAsync(jobId, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        _logger.Received(1).LogError(Arg.Any<Exception>(), Arg.Any<string>());
    }

    /// <summary>
    /// When the operation is cancelled the service should re-throw
    /// <see cref="OperationCanceledException"/> so callers can handle it correctly.
    /// </summary>
    [Fact]
    public async Task CancelAsync_WhenCancelled_ShouldPropagateCancellation()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var jobId = Guid.NewGuid();
        _storage
            .CancelJobAsync(jobId, cts.Token)
            .Returns<bool>(_ => throw new OperationCanceledException());

        // Act
        var act = () => _sut.CancelAsync(jobId, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Verifies that <see cref="CancelJobService.CancelAsync"/> delegates to
    /// <see cref="IDashboardStorage.CancelJobAsync"/> with the same job ID and
    /// cancellation token that were passed in.
    /// </summary>
    [Fact]
    public async Task CancelAsync_ShouldPassJobIdAndCancellationTokenToStorage()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var jobId = Guid.NewGuid();
        _storage.CancelJobAsync(jobId, cts.Token).Returns(true);

        // Act
        await _sut.CancelAsync(jobId, cts.Token);

        // Assert
        await _storage.Received(1).CancelJobAsync(jobId, cts.Token);
    }
}
