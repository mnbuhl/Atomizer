using Atomizer.FlowTests.TestJobs;

namespace Atomizer.FlowTests;

public abstract partial class AtomizerFlowTests
{
    [Fact]
    public async Task DequeueAsync_WhenPendingJobIsCancelledBeforeProcessing_ShouldPersistCancelledStateAndNotDispatch()
    {
        var host = await StartHostAsync(options => options.AutoStart = false);
        var key = NewKey();

        var jobId = await host.Client.EnqueueAsync(
            new FlowPayload(key),
            cancellation: TestContext.Current.CancellationToken
        );
        var dequeued = await host.Client.DequeueAsync(jobId, TestContext.Current.CancellationToken);

        await host.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        dequeued.Should().BeTrue();
        var job = await host.GetJobAsync(jobId, TestContext.Current.CancellationToken);
        job.Should().NotBeNull();
        job!.Status.Should().Be(AtomizerJobStatus.Cancelled);
        _recorder.AttemptsFor(key).Should().BeEmpty();
    }

    [Fact]
    public async Task DequeueAsync_WhenJobAlreadyCompleted_ShouldReturnFalseAndKeepCompletedState()
    {
        var host = await StartHostAsync();
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

        var dequeued = await host.Client.DequeueAsync(jobId, TestContext.Current.CancellationToken);
        var job = await host.GetJobAsync(jobId, TestContext.Current.CancellationToken);

        dequeued.Should().BeFalse();
        job.Should().NotBeNull();
        job!.Status.Should().Be(AtomizerJobStatus.Completed);
        _recorder.AttemptsFor(key).Should().ContainSingle();
    }
}
