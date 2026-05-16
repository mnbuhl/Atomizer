using Atomizer.FlowTests.Infrastructure;
using Atomizer.FlowTests.TestJobs;

namespace Atomizer.FlowTests;

public abstract partial class AtomizerFlowTests
{
    [Fact]
    public async Task EnqueueAsync_WhenHandlerSucceeds_ShouldProcessAndPersistCompletedJob()
    {
        var host = await StartHostAsync();
        var key = NewKey();

        var jobId = await host.Client.EnqueueAsync(
            new FlowPayload(key),
            cancellation: TestContext.Current.CancellationToken
        );

        await _recorder.WaitForCountAsync(key, 1, TestContext.Current.CancellationToken);
        var job = await host.WaitForJobAsync(
            jobId,
            job => job.Status == AtomizerJobStatus.Completed,
            TestContext.Current.CancellationToken
        );

        job.Attempts.Should().Be(1);
        job.CompletedAt.Should().NotBeNull();
        job.FailedAt.Should().BeNull();
        job.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task EnqueueAsync_WhenHandlerFailsWithoutRetry_ShouldPersistFailedJob()
    {
        var host = await StartHostAsync();
        var key = NewKey();

        var jobId = await host.Client.EnqueueAsync(
            new FailingFlowPayload(key),
            options => options.RetryStrategy = RetryStrategy.None,
            TestContext.Current.CancellationToken
        );

        var job = await host.WaitForJobAsync(
            jobId,
            job => job.Status == AtomizerJobStatus.Failed,
            TestContext.Current.CancellationToken
        );

        _recorder.AttemptsFor(key).Should().ContainSingle();
        job.Attempts.Should().Be(1);
        job.CompletedAt.Should().BeNull();
        job.FailedAt.Should().NotBeNull();
        job.Errors.Should().ContainSingle();
    }

    [Fact]
    public async Task EnqueueAsync_WhenHandlerFailsThenSucceeds_ShouldRetryAndPersistCompletedJob()
    {
        var host = await StartHostAsync();
        var key = NewKey();

        var jobId = await host.Client.EnqueueAsync(
            new EventuallySuccessfulFlowPayload(key, FailuresBeforeSuccess: 1),
            options => options.RetryStrategy = RetryStrategy.Fixed(TimeSpan.Zero, maxAttempts: 2),
            TestContext.Current.CancellationToken
        );

        await _recorder.WaitForCountAsync(key, 2, TestContext.Current.CancellationToken);
        var job = await host.WaitForJobAsync(
            jobId,
            job => job.Status == AtomizerJobStatus.Completed,
            TestContext.Current.CancellationToken
        );

        job.Attempts.Should().Be(2);
        job.Errors.Should().ContainSingle();
        job.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task EnqueueAsync_WhenIdempotencyKeyIsReused_ShouldProcessSinglePersistedJob()
    {
        var host = await StartHostAsync();
        var key = NewKey();
        var idempotencyKey = $"idem-{key}";

        var firstJobId = await host.Client.EnqueueAsync(
            new FlowPayload(key),
            options => options.IdempotencyKey = idempotencyKey,
            TestContext.Current.CancellationToken
        );
        var secondJobId = await host.Client.EnqueueAsync(
            new FlowPayload(key),
            options => options.IdempotencyKey = idempotencyKey,
            TestContext.Current.CancellationToken
        );

        secondJobId.Should().Be(firstJobId);
        await host.WaitForJobAsync(
            firstJobId,
            job => job.Status == AtomizerJobStatus.Completed,
            TestContext.Current.CancellationToken
        );
        var jobs = await host.GetJobsAsync(TestContext.Current.CancellationToken);

        jobs.Count(job => job.IdempotencyKey == idempotencyKey).Should().Be(1);
        _recorder.AttemptsFor(key).Should().ContainSingle();
    }

    [Fact]
    public async Task EnqueueAsync_WhenPartitioned_ShouldProcessSamePartitionInOrder()
    {
        var host = await StartHostAsync(options =>
        {
            options.AutoStart = false;
            options.QueueDegreeOfParallelism = 1;
        });
        var key = NewKey();
        var partition = new PartitionKey($"partition-{key}");
        var expectedKeys = Enumerable.Range(1, 4).Select(index => $"{key}-{index}").ToArray();
        var payloadKeysByJobId = new Dictionary<Guid, string>();

        foreach (var payloadKey in expectedKeys)
        {
            var jobId = await host.Client.EnqueueAsync(
                new FlowPayload(payloadKey),
                options => options.PartitionKey = partition,
                TestContext.Current.CancellationToken
            );
            payloadKeysByJobId[jobId] = payloadKey;
        }

        await host.StartAsync(TestContext.Current.CancellationToken);
        await _recorder.WaitUntilAsync(
            attempts => expectedKeys.All(payloadKey => attempts.Any(attempt => attempt.Key == payloadKey)),
            "Partitioned jobs were not all processed.",
            TestContext.Current.CancellationToken
        );

        var persistedOrder = (await host.GetJobsAsync(TestContext.Current.CancellationToken))
            .Where(job => payloadKeysByJobId.ContainsKey(job.Id))
            .OrderBy(job => job.SequenceNumber)
            .ThenBy(job => job.ScheduledAt)
            .ThenBy(job => job.CreatedAt)
            .Select(job => payloadKeysByJobId[job.Id])
            .ToArray();
        var observedKeys = _recorder
            .Attempts.Where(attempt => expectedKeys.Contains(attempt.Key))
            .OrderBy(attempt => attempt.Order)
            .Select(attempt => attempt.Key)
            .ToArray();
        observedKeys.Should().Equal(persistedOrder);
    }

    [Fact]
    public async Task EnqueueAsync_WhenMultipleQueuesConfigured_ShouldProcessEachQueue()
    {
        var host = await StartHostAsync();
        var defaultKey = NewKey();
        var criticalKey = NewKey();

        var defaultJobId = await host.Client.EnqueueAsync(
            new FlowPayload(defaultKey),
            cancellation: TestContext.Current.CancellationToken
        );
        var criticalJobId = await host.Client.EnqueueAsync(
            new FlowPayload(criticalKey),
            options => options.Queue = FlowQueues.Critical,
            TestContext.Current.CancellationToken
        );

        await host.WaitForJobAsync(
            defaultJobId,
            job => job.Status == AtomizerJobStatus.Completed,
            TestContext.Current.CancellationToken
        );
        var criticalJob = await host.WaitForJobAsync(
            criticalJobId,
            job => job.Status == AtomizerJobStatus.Completed,
            TestContext.Current.CancellationToken
        );

        criticalJob.QueueKey.Should().Be(FlowQueues.Critical);
        _recorder.AttemptsFor(defaultKey).Should().ContainSingle();
        _recorder.AttemptsFor(criticalKey).Should().ContainSingle();
    }

    [Fact]
    public async Task EnqueueAsync_WhenQueueIsNotConfigured_ShouldRemainPending()
    {
        var host = await StartHostAsync();
        var key = NewKey();
        var unconfiguredQueue = new QueueKey($"missing-{key}");

        var jobId = await host.Client.EnqueueAsync(
            new FlowPayload(key),
            options => options.Queue = unconfiguredQueue,
            TestContext.Current.CancellationToken
        );

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        var job = await host.GetJobAsync(jobId, TestContext.Current.CancellationToken);
        job.Should().NotBeNull();
        job!.Status.Should().Be(AtomizerJobStatus.Pending);
        job.QueueKey.Should().Be(unconfiguredQueue);
        _recorder.AttemptsFor(key).Should().BeEmpty();
    }

    [Fact]
    public async Task EnqueueAsync_WhenHandlerIsMissing_ShouldPersistFailedJob()
    {
        var host = await StartHostAsync();
        var key = NewKey();

        var jobId = await host.Client.EnqueueAsync(
            new MissingHandlerPayload(key),
            options => options.RetryStrategy = RetryStrategy.None,
            TestContext.Current.CancellationToken
        );

        var job = await host.WaitForJobAsync(
            jobId,
            job => job.Status == AtomizerJobStatus.Failed,
            TestContext.Current.CancellationToken
        );

        _recorder.AttemptsFor(key).Should().BeEmpty();
        job.Attempts.Should().Be(1);
        job.Errors.Should().ContainSingle();
    }

    [Fact]
    public async Task EnqueueAsync_WhenTwoHostsProcessSameQueue_ShouldCompleteEachJobExactlyOnce()
    {
        var firstHost = await StartHostAsync(options => options.AutoStart = false);
        var secondHost = await StartHostAsync(options => options.AutoStart = false);
        var keys = Enumerable.Range(1, 12).Select(_ => NewKey()).ToArray();
        var jobIds = new List<Guid>();

        foreach (var key in keys)
        {
            jobIds.Add(
                await firstHost.Client.EnqueueAsync(
                    new FlowPayload(key),
                    cancellation: TestContext.Current.CancellationToken
                )
            );
        }

        await Task.WhenAll(
            firstHost.StartAsync(TestContext.Current.CancellationToken),
            secondHost.StartAsync(TestContext.Current.CancellationToken)
        );

        await firstHost.WaitForJobsAsync(
            jobs => jobIds.All(jobId => jobs.Any(job => job.Id == jobId && job.Status == AtomizerJobStatus.Completed)),
            "Two processing hosts did not complete every queued job.",
            TestContext.Current.CancellationToken
        );

        foreach (var key in keys)
        {
            _recorder.AttemptsFor(key).Should().ContainSingle();
        }
    }
}
