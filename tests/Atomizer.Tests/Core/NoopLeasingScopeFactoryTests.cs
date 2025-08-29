using Atomizer.Core;

namespace Atomizer.Tests.Core;

public sealed class NoopLeasingScopeFactoryTests
{
    private static QueueKey NewKey() => new QueueKey($"q-{Guid.NewGuid():N}");

    [Fact]
    public async Task CreateScopeAsync_WhenCalled_ShouldReturnAcquiredTrue()
    {
        // Arrange
        var sut = new NoopLeasingScopeFactory();
        var key = NewKey();

        // Act
        var scope = await sut.CreateScopeAsync(key, TimeSpan.FromSeconds(1), CancellationToken.None);

        // Assert
        scope.Acquired.Should().BeTrue();
        scope.Dispose();
    }

    [Fact]
    public async Task CreateScopeAsync_WhenCancellationRequested_ShouldStillReturnScope()
    {
        // Arrange
        var sut = new NoopLeasingScopeFactory();
        var key = NewKey();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // token is already canceled

        // Act
        var scope = await sut.CreateScopeAsync(key, TimeSpan.Zero, cts.Token);

        // Assert
        scope.Acquired.Should().BeTrue("factory ignores cancellation and timeouts");
        scope.Dispose();
    }

    [Fact]
    public async Task CreateScopeAsync_WhenCalledTwice_ShouldReturnDistinctInstances()
    {
        // Arrange
        var sut = new NoopLeasingScopeFactory();
        var key = NewKey();

        // Act
        var s1 = await sut.CreateScopeAsync(key, TimeSpan.FromMinutes(1), CancellationToken.None);
        var s2 = await sut.CreateScopeAsync(key, TimeSpan.FromMinutes(1), CancellationToken.None);

        // Assert
        s1.Should().NotBeSameAs(s2);
        s1.Dispose();
        s2.Dispose();
    }

    [Fact]
    public async Task Dispose_WhenCalled_ShouldNotThrow()
    {
        // Arrange
        var sut = new NoopLeasingScopeFactory();
        var key = NewKey();
        var scope = await sut.CreateScopeAsync(key, TimeSpan.FromSeconds(1), CancellationToken.None);

        // Act & Assert
        var act = () => scope.Dispose();
        act.Should().NotThrow();

        // Idempotency
        scope.Invoking(s => s.Dispose()).Should().NotThrow();
    }

#if NET7_0_OR_GREATER
    [Fact]
    public async Task DisposeAsync_WhenCalled_ShouldNotThrowAndBeIdempotent()
    {
        // Arrange
        var sut = new NoopLeasingScopeFactory();
        var key = NewKey();
        var scope = await sut.CreateScopeAsync(key, TimeSpan.FromSeconds(1), CancellationToken.None);

        // Act & Assert
        await scope.Awaiting(s => s.DisposeAsync().AsTask()).Should().NotThrowAsync();
        await scope.Awaiting(s => s.DisposeAsync().AsTask()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task UsingAwaitUsingPattern_WhenScopeExits_ShouldNotThrow()
    {
        // Arrange
        var sut = new NoopLeasingScopeFactory();
        var key = NewKey();

        // Act / Assert
        using (var scope = await sut.CreateScopeAsync(key, TimeSpan.FromSeconds(1), CancellationToken.None))
        {
            scope.Acquired.Should().BeTrue();
        }
    }
#endif
}
