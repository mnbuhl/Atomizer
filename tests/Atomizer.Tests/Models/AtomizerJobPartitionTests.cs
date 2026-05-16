namespace Atomizer.Tests.Models;

/// <summary>
/// Unit tests for <see cref="AtomizerJob.IsPartitionBlocked"/>.
/// </summary>
public class AtomizerJobPartitionTests
{
    private static AtomizerJob CreateJob(PartitionKey? partitionKey = null)
    {
        return AtomizerJob.Create(
            QueueKey.Default,
            typeof(object),
            "{}",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            partitionKey: partitionKey
        );
    }

    [Fact]
    public void IsPartitionBlocked_WhenPartitionKeyIsNull_ShouldReturnFalse()
    {
        // Arrange
        var job = CreateJob(partitionKey: null);

        // Assert
        job.IsPartitionBlocked.Should().BeFalse();
    }

    [Fact]
    public void IsPartitionBlocked_WhenStatusIsPendingAndAttemptsIsZero_ShouldReturnFalse()
    {
        // Arrange
        var job = CreateJob(partitionKey: new PartitionKey("orders"));
        // Create() already sets Status=Pending, Attempts=0

        // Assert
        job.IsPartitionBlocked.Should().BeFalse();
    }

    [Fact]
    public void IsPartitionBlocked_WhenStatusIsProcessing_ShouldReturnTrue()
    {
        // Arrange
        var job = CreateJob(partitionKey: new PartitionKey("orders"));
        job.Lease(
            new LeaseToken($"worker:*:default:*:{Guid.NewGuid()}"),
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(10)
        );
        // Status is now Processing

        // Assert
        job.IsPartitionBlocked.Should().BeTrue();
    }

    [Fact]
    public void IsPartitionBlocked_WhenStatusIsPendingAndAttemptsGreaterThanZero_ShouldReturnTrue()
    {
        // Arrange
        var job = CreateJob(partitionKey: new PartitionKey("orders"));
        job.Lease(
            new LeaseToken($"worker:*:default:*:{Guid.NewGuid()}"),
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(10)
        );
        job.Attempt();
        job.Reschedule(DateTimeOffset.UtcNow.AddSeconds(15), DateTimeOffset.UtcNow);
        // Status is now Pending, Attempts == 1

        // Assert
        job.IsPartitionBlocked.Should().BeTrue();
    }

    [Fact]
    public void Cancel_WhenJobIsPending_ShouldMarkJobCancelled()
    {
        // Arrange
        var job = CreateJob();
        var cancelledAt = DateTimeOffset.UtcNow;

        // Act
        job.Cancel(cancelledAt);

        // Assert
        job.Status.Should().Be(AtomizerJobStatus.Cancelled);
        job.UpdatedAt.Should().Be(cancelledAt);
    }

    [Fact]
    public void Cancel_WhenJobIsProcessing_ShouldThrow()
    {
        // Arrange
        var job = CreateJob();
        job.Lease(
            new LeaseToken($"worker:*:default:*:{Guid.NewGuid()}"),
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(10)
        );

        // Act
        Action act = () => job.Cancel(DateTimeOffset.UtcNow);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Pending status*");
    }
}
