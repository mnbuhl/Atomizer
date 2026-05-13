using Atomizer.Processing;
using Atomizer.Tests.Utilities.TestJobs;

namespace Atomizer.Tests.Processing;

/// <summary>
/// Unit tests for <see cref="JobBatch"/>.
/// </summary>
public sealed class JobBatchTests
{
    [Fact]
    public void Constructor_WhenJobsIsEmpty_ShouldThrowArgumentException()
    {
        var act = () => new JobBatch(Array.Empty<AtomizerJob>());

        act.Should().Throw<ArgumentException>().WithParameterName("jobs");
    }

    [Fact]
    public void Constructor_WhenJobsIsNull_ShouldThrowArgumentNullException()
    {
        var act = () => new JobBatch(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("jobs");
    }

    [Fact]
    public void IsPartitioned_WhenFirstJobHasPartitionKey_ShouldReturnTrue()
    {
        var now = DateTimeOffset.UtcNow;
        var job = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineJob),
            "{}",
            now,
            now,
            partitionKey: new PartitionKey("customer-1")
        );

        var batch = new JobBatch(new[] { job });

        batch.IsPartitioned.Should().BeTrue();
        batch.FirstJob.Should().Be(job);
        batch.Count.Should().Be(1);
    }

    [Fact]
    public void IsPartitioned_WhenFirstJobHasNoPartitionKey_ShouldReturnFalse()
    {
        var now = DateTimeOffset.UtcNow;
        var job = AtomizerJob.Create(QueueKey.Default, typeof(WriteLineJob), "{}", now, now);

        var batch = new JobBatch(new[] { job });

        batch.IsPartitioned.Should().BeFalse();
    }
}
