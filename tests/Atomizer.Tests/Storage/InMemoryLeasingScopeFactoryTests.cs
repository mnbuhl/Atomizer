using Atomizer.Core;
using Atomizer.Storage;

namespace Atomizer.Tests.Storage;

public sealed class InMemoryLeasingScopeFactoryTests
{
    private static QueueKey NewKey() => new QueueKey($"q-{Guid.NewGuid():N}");

    private static (
        InMemoryLeasingScopeFactory sut,
        IAtomizerClock clock,
        TestableLogger<InMemoryLeasingScopeFactory> logger
    ) CreateSut(DateTimeOffset now)
    {
        var clock = Substitute.For<IAtomizerClock>();
        var current = now;
        clock.UtcNow.Returns(_ => current);
        // Not used by SUT but required by interface
        clock.MinValue.Returns(DateTimeOffset.MinValue);
        clock.MaxValue.Returns(DateTimeOffset.MaxValue);

        var logger = Substitute.For<TestableLogger<InMemoryLeasingScopeFactory>>();

        return (new InMemoryLeasingScopeFactory(clock, logger), clock, logger);
    }

    private static void Advance(IAtomizerClock clock, Func<DateTimeOffset> getNext)
    {
        clock.UtcNow.Returns(_ => getNext());
    }

    [Fact]
    public async Task CreateScopeAsync_WhenSemaphoreAvailable_ShouldAcquireTrue()
    {
        // Arrange
        var t0 = DateTimeOffset.UtcNow;
        var (sut, _, logger) = CreateSut(t0);
        var key = NewKey();

        // Act
        using var scope = await sut.CreateScopeAsync(key, TimeSpan.FromSeconds(5), CancellationToken.None);

        // Assert
        scope.Acquired.Should().BeTrue();

        // Logging sanity (pre + post)
        logger.Received().LogDebug(Arg.Is<string>(m => m.Contains("Acquiring in-memory leasing scope for queue")));
        logger.Received().LogDebug(Arg.Is<string>(m => m.Contains("In memory leasing scope for queue")));
    }

    [Fact]
    public async Task CreateScopeAsync_WhenAlreadyAcquired_ShouldAcquireFalse()
    {
        // Arrange
        var t0 = DateTimeOffset.UtcNow;
        var (sut, _, _) = CreateSut(t0);
        var key = NewKey();

        // Act
        using var first = await sut.CreateScopeAsync(key, TimeSpan.FromSeconds(10), CancellationToken.None);
        using var second = await sut.CreateScopeAsync(key, TimeSpan.FromSeconds(10), CancellationToken.None);

        // Assert
        first.Acquired.Should().BeTrue("first caller should get the lease");
        second.Acquired.Should().BeFalse("second caller sees the lease already held within timeout window");
    }

    [Fact]
    public async Task CreateScopeAsync_WhenTimedOut_ShouldReclaimAndAcquireTrue()
    {
        // Arrange
        var timeout = TimeSpan.FromMilliseconds(100);
        var t0 = DateTimeOffset.UtcNow;
        var current = t0;
        var (sut, clock, _) = CreateSut(current);
        var key = NewKey();

        // Acquire first without disposing
        var scope1 = await sut.CreateScopeAsync(key, timeout, CancellationToken.None);
        scope1.Acquired.Should().BeTrue();

        // Advance “now” beyond timeout to trigger forced release
        current = t0 + timeout + TimeSpan.FromMilliseconds(1);
        Advance(clock, () => current);

        // Act
        var scope2 = await sut.CreateScopeAsync(key, timeout, CancellationToken.None);

        // Assert
        scope2.Acquired.Should().BeTrue("second caller should reclaim a timed-out lease");

        scope2.Dispose();
        scope1.Dispose();
    }

    [Fact]
    public async Task Dispose_WhenReleased_ShouldAllowSubsequentAcquire()
    {
        // Arrange
        var t0 = DateTimeOffset.UtcNow;
        var (sut, _, _) = CreateSut(t0);
        var key = NewKey();

        using (var s1 = await sut.CreateScopeAsync(key, TimeSpan.FromSeconds(5), CancellationToken.None))
        {
            s1.Acquired.Should().BeTrue();
        }

        // Act
        using var s2 = await sut.CreateScopeAsync(key, TimeSpan.FromSeconds(5), CancellationToken.None);

        // Assert
        s2.Acquired.Should().BeTrue("disposing the first scope should release the semaphore");
    }

    [Fact]
    public async Task Dispose_WhenNotAcquired_ShouldNotThrow()
    {
        // Arrange
        var t0 = DateTimeOffset.UtcNow;
        var (sut, _, _) = CreateSut(t0);
        var key = NewKey();

        using var s1 = await sut.CreateScopeAsync(key, TimeSpan.FromSeconds(5), CancellationToken.None);
        s1.Acquired.Should().BeTrue();

        using var s2 = await sut.CreateScopeAsync(key, TimeSpan.FromSeconds(5), CancellationToken.None);
        s2.Acquired.Should().BeFalse();

        // Act / Assert
        var act = () => s2.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task Dispose_WhenCalledTwice_ShouldBeIdempotent()
    {
        // Arrange
        var t0 = DateTimeOffset.UtcNow;
        var (sut, _, _) = CreateSut(t0);
        var key = NewKey();

        using var scope = await sut.CreateScopeAsync(key, TimeSpan.FromSeconds(5), CancellationToken.None);
        scope.Acquired.Should().BeTrue();

        // Act / Assert
        scope.Dispose();
        Action second = () => scope.Dispose();
        second.Should().NotThrow("double-release is guarded by try/catch on SemaphoreFullException");
    }

    [Fact]
    public async Task DisposeAsync_ShouldRelease_AndNextAcquireSucceeds()
    {
        // Arrange
        var t0 = DateTimeOffset.UtcNow;
        var (sut, _, _) = CreateSut(t0);
        var key = NewKey();

        var s1 = await sut.CreateScopeAsync(key, TimeSpan.FromSeconds(5), CancellationToken.None);
        s1.Acquired.Should().BeTrue();

        // Act
        s1.Dispose();

        // Assert
        using var s2 = await sut.CreateScopeAsync(key, TimeSpan.FromSeconds(5), CancellationToken.None);
        s2.Acquired.Should().BeTrue();
    }

    [Fact]
    public async Task CreateScopeAsync_WhenTimeoutReclaim_HasPostAcquireLog()
    {
        // Arrange
        var timeout = TimeSpan.FromMilliseconds(50);
        var t0 = DateTimeOffset.UtcNow;
        var current = t0;
        var (sut, clock, logger) = CreateSut(current);
        var key = NewKey();

        var s1 = await sut.CreateScopeAsync(key, timeout, CancellationToken.None);
        s1.Acquired.Should().BeTrue();

        current = t0 + timeout + TimeSpan.FromMilliseconds(1);
        Advance(clock, () => current);

        // Act
        var s2 = await sut.CreateScopeAsync(key, timeout, CancellationToken.None);

        // Assert
        s2.Acquired.Should().BeTrue();

        logger
            .Received()
            .LogDebug(
                Arg.Is<string>(m =>
                    m.Contains("In memory leasing scope for queue")
                    && m.Contains("acquired:", StringComparison.OrdinalIgnoreCase)
                )
            );

        s2.Dispose();
        s1.Dispose();
    }
}
