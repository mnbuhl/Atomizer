using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Dashboard.Services;
using NSubstitute;

namespace Atomizer.Dashboard.Tests.Services;

public class DashboardCommandServiceTests
{
    [Fact]
    public async Task CancelJobAsync_WhenJobIsLeasedAfterRead_ShouldReturnConflict()
    {
        var now = DateTimeOffset.UtcNow;
        var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "payload", now, now);
        var storage = Substitute.For<IAtomizerStorage>();
        var clock = Substitute.For<IAtomizerClock>();
        var serializer = Substitute.For<IAtomizerJobSerializer>();
        var options = new AtomizerOptions();

        storage.GetJobByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);
        clock.UtcNow.Returns(_ =>
        {
            job.Status = AtomizerJobStatus.Processing;
            return now.AddSeconds(1);
        });

        var service = new DashboardCommandService(storage, clock, serializer, options);

        var result = await service.CancelJobAsync(job.Id, TestContext.Current.CancellationToken);

        result.StatusCode.Should().Be(409);
        result.Message.Should().Be("Job must be in Pending status to cancel.");
        await storage
            .DidNotReceive()
            .UpdateJobsAsync(Arg.Any<IEnumerable<AtomizerJob>>(), Arg.Any<CancellationToken>());
    }
}
