using Atomizer.Exceptions;

namespace Atomizer.Tests.Models.ValueObjects;

/// <summary>
/// Unit tests for <see cref="RetryStrategy"/>.
/// </summary>
public class RetryStrategyTests
{
    [Fact]
    public void Default_ShouldReturnFixedStrategyWithJitter()
    {
        // Arrange & Act
        var strategy = RetryStrategy.Default;

        // Assert
        strategy.MaxAttempts.Should().Be(3);
        strategy.RetryIntervals.Should().HaveCount(3);
        strategy
            .RetryIntervals.All(i => i >= TimeSpan.FromSeconds(12) && i <= TimeSpan.FromSeconds(18))
            .Should()
            .BeTrue();
    }

    [Fact]
    public void None_ShouldReturnStrategyWithOneAttemptAndNoIntervals()
    {
        // Arrange & Act
        var strategy = RetryStrategy.None;

        // Assert
        strategy.MaxAttempts.Should().Be(1);
        strategy.RetryIntervals.Should().BeEmpty();
    }

    [Fact]
    public void Fixed_ShouldReturnCorrectIntervalsWithoutJitter()
    {
        // Arrange & Act
        var strategy = RetryStrategy.Fixed(TimeSpan.FromSeconds(5), 4);

        // Assert
        strategy.MaxAttempts.Should().Be(4);
        strategy.RetryIntervals.Should().AllBeEquivalentTo(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Fixed_WithJitter_ShouldReturnIntervalsWithinExpectedRange()
    {
        // Arrange & Act
        var strategy = RetryStrategy.Fixed(TimeSpan.FromSeconds(10), 2, jitter: true);

        // Assert
        strategy.RetryIntervals.Should().HaveCount(2);
        strategy
            .RetryIntervals.All(i => i >= TimeSpan.FromSeconds(8) && i <= TimeSpan.FromSeconds(12))
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Fixed_WithNegativeDelay_ShouldThrow()
    {
        // Arrange & Act
        Action act = () => RetryStrategy.Fixed(TimeSpan.FromSeconds(-1), 2);

        // Assert
        act.Should().Throw<InvalidRetryStrategyException>().WithMessage("*Delay cannot be negative*");
    }

    [Fact]
    public void Fixed_WithZeroAttempts_ShouldThrow()
    {
        // Arrange & Act
        Action act = () => RetryStrategy.Fixed(TimeSpan.FromSeconds(1), 0);

        // Assert
        act.Should().Throw<InvalidRetryStrategyException>().WithMessage("*MaxAttempts must be at least 1*");
    }

    [Fact]
    public void Intervals_ShouldReturnStrategyWithGivenIntervals()
    {
        // Arrange
        var intervals = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) };

        // Act
        var strategy = RetryStrategy.Intervals(intervals);

        // Assert
        strategy.MaxAttempts.Should().Be(2);
        strategy.RetryIntervals.Should().BeEquivalentTo(intervals);
    }

    [Fact]
    public void Intervals_WithEmpty_ShouldThrow()
    {
        Action act = () => RetryStrategy.Intervals(Array.Empty<TimeSpan>());
        act.Should().Throw<InvalidRetryStrategyException>().WithMessage("*Intervals cannot be null or empty*");
    }

    [Fact]
    public void Intervals_WithNegative_ShouldThrow()
    {
        // Arrange & Act
        Action act = () => RetryStrategy.Intervals(new[] { TimeSpan.FromSeconds(-1) });

        // Assert
        act.Should().Throw<InvalidRetryStrategyException>().WithMessage("*Intervals cannot contain negative values*");
    }

    [Fact]
    public void Exponential_ShouldReturnIncreasingIntervals()
    {
        // Arrange & Act
        var strategy = RetryStrategy.Exponential(TimeSpan.FromSeconds(1), 3, exponent: 2.0);

        // Assert
        strategy.RetryIntervals[0].Should().Be(TimeSpan.FromSeconds(1));
        strategy.RetryIntervals[1].Should().Be(TimeSpan.FromSeconds(2));
        strategy.RetryIntervals[2].Should().Be(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void Exponential_WithMaxInterval_ShouldCapIntervals()
    {
        // Arrange & Act
        var strategy = RetryStrategy.Exponential(
            TimeSpan.FromSeconds(1),
            4,
            exponent: 2.0,
            maxInterval: TimeSpan.FromSeconds(3)
        );

        // Assert
        strategy.RetryIntervals[0].Should().Be(TimeSpan.FromSeconds(1));
        strategy.RetryIntervals[1].Should().Be(TimeSpan.FromSeconds(2));
        strategy.RetryIntervals[2].Should().Be(TimeSpan.FromSeconds(3));
        strategy.RetryIntervals[3].Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Exponential_WithJitter_ShouldReturnIntervalsWithinExpectedRange()
    {
        // Arrange
        var initial = TimeSpan.FromSeconds(2);
        var exponent = 2.0;
        var strategy = RetryStrategy.Exponential(initial, 2, exponent: exponent, jitter: true);

        // Act
        var intervals = strategy.RetryIntervals;

        // Assert
        intervals.Should().HaveCount(2);
        intervals[0].Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1.6));
        intervals[0].Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(2.4));
        intervals[1].Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(3.2));
        intervals[1].Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(4.8));
    }

    [Fact]
    public void Exponential_WithInvalidInitialInterval_ShouldThrow()
    {
        // Arrange & Act
        Action act = () => RetryStrategy.Exponential(TimeSpan.Zero, 2);

        // Assert
        act.Should().Throw<InvalidRetryStrategyException>().WithMessage("*InitialInterval must be greater than zero*");
    }

    [Fact]
    public void Exponential_WithInvalidExponent_ShouldThrow()
    {
        // Arrange & Act
        Action act = () => RetryStrategy.Exponential(TimeSpan.FromSeconds(1), 2, exponent: 1.0);

        // Assert
        act.Should().Throw<InvalidRetryStrategyException>().WithMessage("*Exponent must be greater than 1.0*");
    }

    [Fact]
    public void Exponential_WithInvalidMaxInterval_ShouldThrow()
    {
        // Arrange & Act
        Action act = () => RetryStrategy.Exponential(TimeSpan.FromSeconds(1), 2, maxInterval: TimeSpan.Zero);

        // Assert
        act.Should().Throw<InvalidRetryStrategyException>().WithMessage("*MaxInterval must be greater than zero*");
    }

    [Fact]
    public void ShouldRetry_ShouldReturnTrueForAttemptsLessThanMax()
    {
        // Arrange & Act
        var strategy = RetryStrategy.Fixed(TimeSpan.FromSeconds(1), 3);

        // Assert
        strategy.ShouldRetry(0).Should().BeTrue();
        strategy.ShouldRetry(2).Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_ShouldReturnFalseForAttemptsEqualOrGreaterThanMax()
    {
        // Arrange & Act
        var strategy = RetryStrategy.Fixed(TimeSpan.FromSeconds(1), 2);

        // Assert
        strategy.ShouldRetry(2).Should().BeFalse();
        strategy.ShouldRetry(3).Should().BeFalse();
    }

    [Fact]
    public void GetRetryInterval_ShouldReturnCorrectInterval()
    {
        // Arrange & Act
        var strategy = RetryStrategy.Fixed(TimeSpan.FromSeconds(5), 2);

        // Assert
        strategy.GetRetryInterval(1).Should().Be(TimeSpan.FromSeconds(5));
        strategy.GetRetryInterval(2).Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GetRetryInterval_WithInvalidAttempt_ShouldThrow()
    {
        // Arrange & Act
        var strategy = RetryStrategy.Fixed(TimeSpan.FromSeconds(5), 2);

        // Assert
        Action act1 = () => strategy.GetRetryInterval(0);
        Action act2 = () => strategy.GetRetryInterval(3);
        act1.Should().Throw<ArgumentOutOfRangeException>();
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }
}
