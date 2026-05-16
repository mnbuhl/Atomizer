using Atomizer.FlowTests.TestJobs;

namespace Atomizer.FlowTests;

public abstract partial class AtomizerFlowTests
{
    [Fact]
    public async Task ExecuteAsync_WhenHandlerSucceeds_ShouldPersistCompletedJobImmediately()
    {
        var host = await StartHostAsync(options => options.AddProcessing = false);
        var key = NewKey();

        var jobId = await host.Client.ExecuteAsync(
            new FlowPayload(key),
            cancellation: TestContext.Current.CancellationToken
        );

        var job = await host.GetJobAsync(jobId, TestContext.Current.CancellationToken);
        job.Should().NotBeNull();
        job!.Status.Should().Be(AtomizerJobStatus.Completed);
        job.Attempts.Should().Be(1);
        _recorder.AttemptsFor(key).Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteAsync_WhenHandlerThrows_ShouldPersistFailedJobAndRethrow()
    {
        var host = await StartHostAsync(options => options.AddProcessing = false);
        var key = NewKey();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.Client.ExecuteAsync(new FailingFlowPayload(key), cancellation: TestContext.Current.CancellationToken)
        );

        exception.Message.Should().Be(key);
        var attempt = _recorder.AttemptsFor(key).Should().ContainSingle().Subject;
        var job = await host.GetJobAsync(attempt.JobId, TestContext.Current.CancellationToken);
        job.Should().NotBeNull();
        job!.Status.Should().Be(AtomizerJobStatus.Failed);
        job.Errors.Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteAsync_WhenIdempotencyKeyAlreadyExists_ShouldNotDispatchDuplicateExecution()
    {
        var host = await StartHostAsync(options => options.AddProcessing = false);
        var firstKey = NewKey();
        var secondKey = NewKey();
        var idempotencyKey = $"direct-{firstKey}";

        var firstJobId = await host.Client.ExecuteAsync(
            new FlowPayload(firstKey),
            options => options.IdempotencyKey = idempotencyKey,
            TestContext.Current.CancellationToken
        );
        var secondJobId = await host.Client.ExecuteAsync(
            new FlowPayload(secondKey),
            options => options.IdempotencyKey = idempotencyKey,
            TestContext.Current.CancellationToken
        );

        secondJobId.Should().Be(firstJobId);
        _recorder.AttemptsFor(firstKey).Should().ContainSingle();
        _recorder.AttemptsFor(secondKey).Should().BeEmpty();
        var jobs = await host.GetJobsAsync(TestContext.Current.CancellationToken);
        jobs.Count(job => job.IdempotencyKey == idempotencyKey).Should().Be(1);
    }
}
