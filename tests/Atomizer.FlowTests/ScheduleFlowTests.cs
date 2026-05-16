using Atomizer.FlowTests.Infrastructure;
using Atomizer.FlowTests.TestJobs;

namespace Atomizer.FlowTests;

public abstract partial class AtomizerFlowTests
{
    [Fact]
    public async Task ScheduleAsync_WhenRunAtArrives_ShouldWaitUntilDueThenProcess()
    {
        var host = await StartHostAsync();
        var key = NewKey();
        var runAt = TruncateToMilliseconds(DateTimeOffset.UtcNow.AddSeconds(2));

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

    private static DateTimeOffset TruncateToMilliseconds(DateTimeOffset value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, value.Offset);

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
        var schedules = await host.WaitForSchedulesAsync(
            schedules => schedules.Single(schedule => schedule.JobKey == scheduleKey).LastEnqueueAt is not null,
            $"Recurring schedule {scheduleKey} did not advance.",
            TestContext.Current.CancellationToken
        );
        var schedule = schedules.Single(schedule => schedule.JobKey == scheduleKey);

        jobs.Count(job => job.ScheduleJobKey == scheduleKey).Should().BeGreaterThanOrEqualTo(1);
        schedule.NextRunAt.Should().BeAfter(schedule.LastEnqueueAt!.Value);
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
    public async Task ScheduleRecurringAsync_WhenDisabledScheduleIsDue_ShouldNotEnqueueOccurrence()
    {
        var host = await StartHostAsync(options => options.AutoStart = false);
        var key = NewKey();
        var scheduleKey = new JobKey($"disabled-{key}");
        var now = DateTimeOffset.UtcNow;

        await host.Client.ScheduleRecurringAsync(
            new FlowPayload(key),
            scheduleKey,
            Schedule.Every().Second(),
            options => options.Enabled = false,
            TestContext.Current.CancellationToken
        );
        await MoveScheduleIntoPastAsync(host, scheduleKey, now.AddSeconds(-2), now.AddSeconds(-1));

        await host.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        var schedules = await host.GetSchedulesAsync(TestContext.Current.CancellationToken);
        schedules.Single(schedule => schedule.JobKey == scheduleKey).Enabled.Should().BeFalse();
        var jobs = await host.GetJobsAsync(TestContext.Current.CancellationToken);
        jobs.Should().NotContain(job => job.ScheduleJobKey == scheduleKey);
        _recorder.AttemptsFor(key).Should().BeEmpty();
    }

    [Fact]
    public async Task ScheduleRecurringAsync_WhenOptionsAreConfigured_ShouldPropagateToEnqueuedJob()
    {
        var host = await StartHostAsync(options => options.AutoStart = false);
        var key = NewKey();
        var scheduleKey = new JobKey($"options-{key}");
        var partition = new PartitionKey($"partition-{key}");
        var now = DateTimeOffset.UtcNow;

        await host.Client.ScheduleRecurringAsync(
            new FailingFlowPayload(key),
            scheduleKey,
            Schedule.Every().Hour(),
            options =>
            {
                options.Queue = FlowQueues.Critical;
                options.PartitionKey = partition;
                options.RetryStrategy = RetryStrategy.None;
                options.MisfirePolicy = MisfirePolicy.ExecuteNow;
            },
            TestContext.Current.CancellationToken
        );
        await MoveScheduleIntoPastAsync(host, scheduleKey, now.AddHours(-2), now.AddHours(-1));

        await host.StartAsync(TestContext.Current.CancellationToken);

        var jobs = await host.WaitForJobsAsync(
            jobs =>
                jobs.Any(job =>
                    job.ScheduleJobKey == scheduleKey
                    && job.Status == AtomizerJobStatus.Failed
                    && job.QueueKey == FlowQueues.Critical
                ),
            $"Recurring schedule {scheduleKey} did not enqueue a failed critical-queue job.",
            TestContext.Current.CancellationToken
        );
        var job = jobs.Single(job => job.ScheduleJobKey == scheduleKey);

        job.QueueKey.Should().Be(FlowQueues.Critical);
        job.PartitionKey.Should().Be(partition);
        job.Attempts.Should().Be(1);
        job.Errors.Should().ContainSingle();
        _recorder.AttemptsFor(key).Should().ContainSingle();
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
    public async Task ScheduleRecurringAsync_WhenTwoSchedulersSeeSameDueSchedule_ShouldEnqueueSingleOccurrence()
    {
        var firstHost = await StartHostAsync(options => options.AutoStart = false);
        var secondHost = await StartHostAsync(options => options.AutoStart = false);
        var key = NewKey();
        var scheduleKey = new JobKey($"distributed-{key}");
        var now = DateTimeOffset.UtcNow;

        await firstHost.Client.ScheduleRecurringAsync(
            new FlowPayload(key),
            scheduleKey,
            Schedule.Every().Hour(),
            options => options.MisfirePolicy = MisfirePolicy.ExecuteNow,
            TestContext.Current.CancellationToken
        );
        await MoveScheduleIntoPastAsync(firstHost, scheduleKey, now.AddHours(-2), now.AddHours(-1));

        await Task.WhenAll(
            firstHost.StartAsync(TestContext.Current.CancellationToken),
            secondHost.StartAsync(TestContext.Current.CancellationToken)
        );

        await firstHost.WaitForJobsAsync(
            jobs =>
            {
                var scheduleJobs = jobs.Where(job => job.ScheduleJobKey == scheduleKey).ToArray();
                return scheduleJobs.Length == 1 && scheduleJobs[0].Status == AtomizerJobStatus.Completed;
            },
            $"Distributed recurring schedule {scheduleKey} did not produce one completed occurrence.",
            TestContext.Current.CancellationToken
        );
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        var jobs = await firstHost.GetJobsAsync(TestContext.Current.CancellationToken);
        var scheduleJobs = jobs.Where(job => job.ScheduleJobKey == scheduleKey).ToArray();
        scheduleJobs.Should().ContainSingle();
        scheduleJobs[0].Status.Should().Be(AtomizerJobStatus.Completed);
        _recorder.AttemptsFor(key).Should().ContainSingle();
    }
}
