using Atomizer.EntityFrameworkCore.Storage;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using NSubstitute;

namespace Atomizer.EntityFrameworkCore.Tests.Storage;

public sealed class DatabaseTransactionLeasingScopeTests
{
    [Fact]
    public void Ctor_WhenTransactionProvided_ShouldSetAcquiredTrue()
    {
        // Arrange
        var tx = Substitute.For<IDbContextTransaction>();

        // Act
        var scope = new DatabaseTransactionLeasingScope(tx);

        // Assert
        scope.Acquired.Should().BeTrue();
    }

    [Fact]
    public void Ctor_WhenTransactionNull_ShouldSetAcquiredFalse()
    {
        // Act
        var scope = new DatabaseTransactionLeasingScope(null);

        // Assert
        scope.Acquired.Should().BeFalse();
    }

    [Fact]
    public void Dispose_WhenCommitSucceeds_ShouldCommitAndDispose()
    {
        // Arrange
        var tx = Substitute.For<IDbContextTransaction>();
        var scope = new DatabaseTransactionLeasingScope(tx);

        // Act
        scope.Dispose();

        // Assert
        Received.InOrder(() =>
        {
            tx.Commit();
            tx.Dispose();
        });
        tx.DidNotReceive().Rollback();
    }

    [Fact]
    public void Dispose_WhenCommitThrows_ShouldRollbackThenRethrow()
    {
        // Arrange
        var tx = Substitute.For<IDbContextTransaction>();
        tx.When(t => t.Commit()).Do(_ => throw new InvalidOperationException("failure"));
        var scope = new DatabaseTransactionLeasingScope(tx);

        // Act
        var act = () => scope.Dispose();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*failure*");
        Received.InOrder(() =>
        {
            tx.Commit();
            tx.Rollback();
            tx.Dispose();
        });
    }

    [Fact]
    public async Task DisposeAsync_WhenCommitSucceeds_ShouldCommitAndDisposeAsync()
    {
        // Arrange
        var tx = Substitute.For<IDbContextTransaction>();
        var scope = new DatabaseTransactionLeasingScope(tx);

        // Act
        await scope.DisposeAsync();

        // Assert
        Received.InOrder(async void () =>
        {
            await tx.CommitAsync();
            await tx.DisposeAsync();
        });
        await tx.DidNotReceive().RollbackAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DisposeAsync_WhenCommitThrows_ShouldRollbackAsyncThenRethrow()
    {
        // Arrange
        var tx = Substitute.For<IDbContextTransaction>();
        tx.When(t => t.CommitAsync(CancellationToken.None)).Do(_ => throw new InvalidOperationException("boom"));
        var scope = new DatabaseTransactionLeasingScope(tx);

        // Act
        var act = async () => await scope.DisposeAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*boom*");
        Received.InOrder(async void () =>
        {
            await tx.CommitAsync();
            await tx.RollbackAsync();
            await tx.DisposeAsync();
        });
    }

    [Fact]
    public void Dispose_WhenTransactionNull_ShouldNoop()
    {
        // Arrange
        var scope = new DatabaseTransactionLeasingScope(null);

        // Act / Assert
        scope.Invoking(s => s.Dispose()).Should().NotThrow();
    }

    [Fact]
    public async Task DisposeAsync_WhenTransactionNull_ShouldNoop()
    {
        // Arrange
        var scope = new DatabaseTransactionLeasingScope(null);

        // Act / Assert
        await scope.Awaiting(s => s.DisposeAsync().AsTask()).Should().NotThrowAsync();
    }
}
