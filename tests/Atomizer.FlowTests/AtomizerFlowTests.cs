using Atomizer.FlowTests.Drivers;
using Atomizer.FlowTests.Infrastructure;
using Atomizer.FlowTests.TestJobs;

namespace Atomizer.FlowTests;

public abstract class AtomizerFlowTests : IAsyncLifetime
{
    private readonly FlowTestDriver _driver;
    private readonly List<FlowTestHost> _hosts = new List<FlowTestHost>();
    private readonly string _runId = Guid.NewGuid().ToString("N");
    private FlowTestRecorder _recorder = null!;

    protected AtomizerFlowTests(FlowTestDriver driver)
    {
        _driver = driver;
    }

    public async ValueTask InitializeAsync()
    {
        _recorder = new FlowTestRecorder();
        await _driver.ResetAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts.AsEnumerable().Reverse())
        {
            await host.DisposeAsync();
        }

        await _driver.ResetAsync(CancellationToken.None);
    }

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
    public async Task ScheduleAsync_WhenRunAtArrives_ShouldWaitUntilDueThenProcess()
    {
        var host = await StartHostAsync();
        var key = NewKey();
        var runAt = DateTimeOffset.UtcNow.AddSeconds(2);

        var jobId = await host.Client.ScheduleAsync(
            new FlowPayload(key),
            runAt,
            cancellation: TestContext.Current.CancellationToken
        );

        await Task.Delay(TimeSpan.FromMilliseconds(350), TestContext.Current.CancellationToken);
        _recorder.AttemptsFor(key).Should().BeEmpty();
        var pending = await host.GetJobAsync(jobId, TestContext.Current.CancellationToken);
        pending.Should().NotBeNull();
        pending!.Status.Should().Be(AtomizerJobStatus.Pending);

        var completed = await host.WaitForJobAsync(
            jobId,
            job => job.Status == AtomizerJobStatus.Completed,
            TestContext.Current.CancellationToken
        );

        completed.ScheduledAt.Should().Be(runAt);
        _recorder.AttemptsFor(key).Should().ContainSingle();
    }

    [Fact]
    public async Task ScheduleRecurringAsync_WhenOccurrenceIsDue_ShouldEnqueueProcessAndAdvanceSchedule()
    {
        var host = await StartHostAsync();
        var key = NewKey();
        var scheduleKey = new JobKey($"recurring-{key}");

        await host.Client.ScheduleRecurringAsync(
            new FlowPayload(key),
            scheduleKey,
            Schedule.Every().Second(),
            cancellation: TestContext.Current.CancellationToken
        );

        await _recorder.WaitForCountAsync(key, 1, TestContext.Current.CancellationToken);
        var jobs = await host.WaitForJobsAsync(
            jobs =>
                jobs.Any(job =>
                    job.ScheduleJobKey == scheduleKey
                    && job.PayloadType == typeof(FlowPayload)
                    && job.Status == AtomizerJobStatus.Completed
                ),
            $"Recurring schedule {scheduleKey} did not enqueue and complete a job.",
            TestContext.Current.CancellationToken
        );
        var schedules = await host.GetSchedulesAsync(TestContext.Current.CancellationToken);
        var schedule = schedules.Single(schedule => schedule.JobKey == scheduleKey);

        jobs.Count(job => job.ScheduleJobKey == scheduleKey).Should().BeGreaterThanOrEqualTo(1);
        schedule.LastEnqueueAt.Should().NotBeNull();
        schedule.NextRunAt.Should().BeAfter(schedule.LastEnqueueAt!.Value);
    }

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

        foreach (var payloadKey in expectedKeys)
        {
            await host.Client.EnqueueAsync(
                new FlowPayload(payloadKey),
                options => options.PartitionKey = partition,
                TestContext.Current.CancellationToken
            );
        }

        await host.StartAsync(TestContext.Current.CancellationToken);
        await _recorder.WaitUntilAsync(
            attempts => expectedKeys.All(payloadKey => attempts.Any(attempt => attempt.Key == payloadKey)),
            "Partitioned jobs were not all processed.",
            TestContext.Current.CancellationToken
        );

        var observedKeys = _recorder
            .Attempts.Where(attempt => expectedKeys.Contains(attempt.Key))
            .OrderBy(attempt => attempt.Order)
            .Select(attempt => attempt.Key)
            .ToArray();
        observedKeys.Should().Equal(expectedKeys);
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
    public async Task ScheduleRecurringAsync_WhenCatchUpMisfires_ShouldEnqueueBoundedCatchUpJobs()
    {
        var host = await StartHostAsync(options => options.AutoStart = false);
        var key = NewKey();
        var scheduleKey = new JobKey($"catchup-{key}");
        var now = DateTimeOffset.UtcNow;

        await host.Client.ScheduleRecurringAsync(
            new FlowPayload(key),
            scheduleKey,
            Schedule.Every(10).Seconds(),
            options =>
            {
                options.MisfirePolicy = MisfirePolicy.CatchUp;
                options.MaxCatchUp = 2;
            },
            TestContext.Current.CancellationToken
        );
        await MoveScheduleIntoPastAsync(host, scheduleKey, now.AddSeconds(-31), now.AddSeconds(-30));

        await host.StartAsync(TestContext.Current.CancellationToken);

        var jobs = await host.WaitForJobsAsync(
            jobs =>
            {
                var scheduleJobs = jobs.Where(job => job.ScheduleJobKey == scheduleKey).ToArray();
                return scheduleJobs.Length == 2 && scheduleJobs.All(job => job.Status == AtomizerJobStatus.Completed);
            },
            $"Catch-up schedule {scheduleKey} did not enqueue exactly two completed jobs.",
            TestContext.Current.CancellationToken
        );

        jobs.Count(job => job.ScheduleJobKey == scheduleKey).Should().Be(2);
        _recorder.AttemptsFor(key).Should().HaveCount(2);
    }

    [Fact]
    public async Task ScheduleRecurringAsync_WhenMisfireIsIgnored_ShouldAdvanceWithoutEnqueuing()
    {
        var host = await StartHostAsync(options => options.AutoStart = false);
        var key = NewKey();
        var scheduleKey = new JobKey($"ignore-{key}");
        var now = DateTimeOffset.UtcNow;

        await host.Client.ScheduleRecurringAsync(
            new FlowPayload(key),
            scheduleKey,
            Schedule.Every().Hour(),
            options => options.MisfirePolicy = MisfirePolicy.Ignore,
            TestContext.Current.CancellationToken
        );
        await MoveScheduleIntoPastAsync(host, scheduleKey, now.AddHours(-2), now.AddHours(-1));

        await host.StartAsync(TestContext.Current.CancellationToken);

        await host.WaitForSchedulesAsync(
            schedules => schedules.Single(schedule => schedule.JobKey == scheduleKey).LastEnqueueAt is not null,
            $"Ignore schedule {scheduleKey} did not advance.",
            TestContext.Current.CancellationToken
        );
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        var jobs = await host.GetJobsAsync(TestContext.Current.CancellationToken);
        jobs.Should().NotContain(job => job.ScheduleJobKey == scheduleKey);
        _recorder.AttemptsFor(key).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteRecurringAsync_WhenScheduleIsDeletedBeforeStart_ShouldNotEnqueueOccurrence()
    {
        var host = await StartHostAsync(options => options.AutoStart = false);
        var key = NewKey();
        var scheduleKey = new JobKey($"deleted-{key}");

        await host.Client.ScheduleRecurringAsync(
            new FlowPayload(key),
            scheduleKey,
            Schedule.Every().Second(),
            cancellation: TestContext.Current.CancellationToken
        );
        var deleted = await host.Client.DeleteRecurringAsync(scheduleKey, TestContext.Current.CancellationToken);

        await host.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        deleted.Should().BeTrue();
        var schedules = await host.GetSchedulesAsync(TestContext.Current.CancellationToken);
        var jobs = await host.GetJobsAsync(TestContext.Current.CancellationToken);
        schedules.Should().NotContain(schedule => schedule.JobKey == scheduleKey);
        jobs.Should().NotContain(job => job.ScheduleJobKey == scheduleKey);
        _recorder.AttemptsFor(key).Should().BeEmpty();
    }

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

    private async Task<FlowTestHost> StartHostAsync(Action<FlowHostOptions>? configure = null)
    {
        var host = await FlowTestHost.CreateAsync(
            _driver,
            _runId,
            _recorder,
            configure,
            TestContext.Current.CancellationToken
        );
        _hosts.Add(host);
        return host;
    }

    private async Task MoveScheduleIntoPastAsync(
        FlowTestHost host,
        JobKey scheduleKey,
        DateTimeOffset lastEnqueueAt,
        DateTimeOffset nextRunAt
    )
    {
        var schedule = (await host.GetSchedulesAsync(TestContext.Current.CancellationToken)).Single(schedule =>
            schedule.JobKey == scheduleKey
        );
        schedule.LastEnqueueAt = lastEnqueueAt;
        schedule.NextRunAt = nextRunAt;
        schedule.CreatedAt = lastEnqueueAt.AddSeconds(-10);
        await host.UpdateSchedulesAsync(new[] { schedule }, TestContext.Current.CancellationToken);
    }

    private async Task WaitUntilDeletedAsync(FlowTestHost host, Guid jobId)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(FlowTestTimings.WaitTimeout);

        while (true)
        {
            if (await host.GetJobAsync(jobId, timeout.Token) is null)
                return;

            try
            {
                await Task.Delay(FlowTestTimings.PollInterval, timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                throw new TimeoutException($"Job {jobId} was not deleted by retention.");
            }
        }
    }

    private string NewKey() => $"{_driver.Name.Replace(" ", string.Empty).ToLowerInvariant()}-{Guid.NewGuid():N}";
}

[Collection(nameof(InMemoryFlowTestDriver))]
public sealed class InMemoryAtomizerFlowTests(InMemoryFlowTestDriver driver) : AtomizerFlowTests(driver);

[Collection(nameof(PostgreSqlFlowTestDriver))]
public sealed class PostgreSqlAtomizerFlowTests(PostgreSqlFlowTestDriver driver) : AtomizerFlowTests(driver);

[Collection(nameof(MySqlFlowTestDriver))]
public sealed class MySqlAtomizerFlowTests(MySqlFlowTestDriver driver) : AtomizerFlowTests(driver);

[Collection(nameof(SqlServerFlowTestDriver))]
public sealed class SqlServerAtomizerFlowTests(SqlServerFlowTestDriver driver) : AtomizerFlowTests(driver);

[Collection(nameof(RedisFlowTestDriver))]
public sealed class RedisAtomizerFlowTests(RedisFlowTestDriver driver) : AtomizerFlowTests(driver);
