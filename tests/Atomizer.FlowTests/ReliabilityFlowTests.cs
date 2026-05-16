using Atomizer.FlowTests.TestJobs;

namespace Atomizer.FlowTests;

public abstract partial class AtomizerFlowTests
{
    [Fact]
    public async Task HeartbeatRecovery_WhenStaleServerOwnsProcessingJob_ShouldReleaseAndProcessJob()
    {
        var host = await StartHostAsync(options => options.AutoStart = false);
        var key = NewKey();
        var staleInstanceId = $"stale-{key}";
        var jobId = await host.Client.EnqueueAsync(
            new FlowPayload(key),
            cancellation: TestContext.Current.CancellationToken
        );
        var job = await host.GetJobAsync(jobId, TestContext.Current.CancellationToken);
        job.Should().NotBeNull();
        job!.Lease(
            new LeaseToken(
                $"{staleInstanceId}{LeaseToken.Delimiter}{QueueKey.Default}{LeaseToken.Delimiter}{Guid.NewGuid():N}"
            ),
            DateTimeOffset.UtcNow.AddSeconds(-10),
            TimeSpan.FromMinutes(10)
        );
        await host.UpdateJobsAsync(new[] { job }, TestContext.Current.CancellationToken);
        await host.UpsertHeartbeatAsync(
            new AtomizerActiveServer
            {
                InstanceId = staleInstanceId,
                LastHeartbeatAt = DateTimeOffset.UtcNow.AddSeconds(-10),
            },
            TestContext.Current.CancellationToken
        );

        await host.StartAsync(TestContext.Current.CancellationToken);

        var completed = await host.WaitForJobAsync(
            jobId,
            job => job.Status == AtomizerJobStatus.Completed,
            TestContext.Current.CancellationToken
        );
        completed.LeaseToken.Should().BeNull();
        _recorder.AttemptsFor(key).Should().ContainSingle();
    }

    [Fact]
    public async Task VisibilityTimeout_WhenProcessingLeaseExpires_ShouldRetryAndCompleteJob()
    {
        var host = await StartHostAsync(options => options.AutoStart = false);
        var key = NewKey();
        var staleInstanceId = $"expired-{key}";
        var jobId = await host.Client.EnqueueAsync(
            new FlowPayload(key),
            cancellation: TestContext.Current.CancellationToken
        );
        var job = await host.GetJobAsync(jobId, TestContext.Current.CancellationToken);
        job.Should().NotBeNull();
        job!.Lease(
            new LeaseToken(
                $"{staleInstanceId}{LeaseToken.Delimiter}{QueueKey.Default}{LeaseToken.Delimiter}{Guid.NewGuid():N}"
            ),
            DateTimeOffset.UtcNow.AddSeconds(-10),
            TimeSpan.FromMilliseconds(100)
        );
        await host.UpdateJobsAsync(new[] { job }, TestContext.Current.CancellationToken);

        await host.StartAsync(TestContext.Current.CancellationToken);

        var completed = await host.WaitForJobAsync(
            jobId,
            job => job.Status == AtomizerJobStatus.Completed,
            TestContext.Current.CancellationToken
        );
        completed.LeaseToken.Should().BeNull();
        _recorder.AttemptsFor(key).Should().ContainSingle();
    }

    [Fact]
    public async Task StopAsync_WhenInFlightJobExceedsGracePeriod_ShouldReleaseForNextHost()
    {
        var key = NewKey();
        var firstHost = await StartHostAsync(options =>
        {
            options.GracefulShutdownTimeout = TimeSpan.FromMilliseconds(100);
        });
        var jobId = await firstHost.Client.EnqueueAsync(
            new StopAndResumeFlowPayload(key),
            cancellation: TestContext.Current.CancellationToken
        );

        await _recorder.WaitForCountAsync(key, 1, TestContext.Current.CancellationToken);
        await firstHost.StopAsync(TestContext.Current.CancellationToken);
        var released = await firstHost.WaitForJobAsync(
            jobId,
            job => job.Status == AtomizerJobStatus.Pending,
            TestContext.Current.CancellationToken
        );
        released.LeaseToken.Should().BeNull();

        var secondHost = await StartHostAsync();
        var completed = await secondHost.WaitForJobAsync(
            jobId,
            job => job.Status == AtomizerJobStatus.Completed,
            TestContext.Current.CancellationToken
        );

        completed.Attempts.Should().Be(1);
        _recorder.AttemptsFor(key).Should().HaveCount(2);
    }

    [Fact]
    public async Task JobRetention_WhenTerminalJobExpires_ShouldDeleteJob()
    {
        var host = await StartHostAsync(options => options.JobRetention = TimeSpan.FromMilliseconds(200));
        var key = NewKey();

        var jobId = await host.Client.EnqueueAsync(
            new FlowPayload(key),
            cancellation: TestContext.Current.CancellationToken
        );
        await host.WaitForJobAsync(
            jobId,
            job => job.Status == AtomizerJobStatus.Completed,
            TestContext.Current.CancellationToken
        );

        await WaitUntilDeletedAsync(host, jobId);
    }
}
