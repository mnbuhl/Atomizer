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
        var job = CreateJob(partitionKey: "orders");
        // Create() already sets Status=Pending, Attempts=0

        // Assert
        job.IsPartitionBlocked.Should().BeFalse();
    }

    [Fact]
    public void IsPartitionBlocked_WhenStatusIsProcessing_ShouldReturnTrue()
    {
        // Arrange
        var job = CreateJob(partitionKey: "orders");
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
        var job = CreateJob(partitionKey: "orders");
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
}
