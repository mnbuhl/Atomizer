using System.Collections.Concurrent;
using Atomizer.Core;
using Atomizer.Storage;

namespace Atomizer.Tests.Storage;

/// <summary>
/// Unit tests for <see cref="InMemoryStorage"/> lease behaviour (ExecuteInLeaseAsync overloads
/// and UpsertScheduleAsync atomicity via the shared <c>QueueKey.Scheduler</c> semaphore).
/// </summary>
public sealed class InMemoryStorageLeaseTests
{
    private static QueueKey NewKey() => new($"q-{Guid.NewGuid():N}");

    private static InMemoryStorage CreateSut()
    {
        var clock = Substitute.For<IAtomizerClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        clock.MinValue.Returns(DateTimeOffset.MinValue);
        clock.MaxValue.Returns(DateTimeOffset.MaxValue);
        var logger = Substitute.For<TestableLogger<InMemoryStorage>>();
        var options = new InMemoryJobStorageOptions { AmountOfJobsToRetainInMemory = 100 };
        return new InMemoryStorage(options, clock, logger);
    }

    // ---- INMEM-02: semaphore held for full callback duration ----

    [Fact]
    public async Task ExecuteInLeaseAsync_WhenCallbackCompletes_ShouldReleaseSemaphore()
    {
        var sut = CreateSut();
        var key = NewKey();

        await sut.ExecuteInLeaseAsync<int>(key, _ => Task.FromResult(42), CancellationToken.None);

        var semaphores = NonPublicSpy.GetFieldValue<InMemoryStorage, ConcurrentDictionary<QueueKey, SemaphoreSlim>>(
            "_semaphores",
            sut
        );
        semaphores[key].CurrentCount.Should().Be(1, "semaphore must be released after callback completes");
    }

    // ---- INMEM-01/INMEM-02: concurrent callers serialized; second skips ----

    [Fact]
    public async Task ExecuteInLeaseAsync_WhenAlreadyAcquired_ShouldSkipCallbackAndReturnDefault()
    {
        var sut = CreateSut();
        var key = NewKey();
        var callbackInvocations = 0;

        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstTask = sut.ExecuteInLeaseAsync<int>(
            key,
            async ct =>
            {
                firstStarted.SetResult(true);
                await firstRelease.Task;
                callbackInvocations++;
                return 1;
            },
            CancellationToken.None
        );

        await firstStarted.Task; // first caller holds the semaphore

        // Second caller — lease is held, should skip
        var result = await sut.ExecuteInLeaseAsync<int>(
            key,
            ct =>
            {
                callbackInvocations++;
                return Task.FromResult(99);
            },
            CancellationToken.None
        );

        result.Should().Be(default(int), "second caller must get default when lease is already held");
        callbackInvocations.Should().Be(0, "second caller's callback must not be invoked");

        firstRelease.SetResult(true);
        await firstTask;
        callbackInvocations.Should().Be(1, "first caller's callback must complete after release");
    }

    // ---- INMEM-02: exception releases semaphore (D-04) ----

    [Fact]
    public async Task ExecuteInLeaseAsync_WhenCallbackThrows_ShouldReleaseSemaphore()
    {
        var sut = CreateSut();
        var key = NewKey();

        // First call — throws
        var act = async () =>
            await sut.ExecuteInLeaseAsync<int>(
                key,
                _ => throw new InvalidOperationException("boom"),
                CancellationToken.None
            );
        await act.Should().ThrowAsync<InvalidOperationException>("exception must propagate unchanged");

        // Semaphore must be released; subsequent call must acquire successfully
        var result = await sut.ExecuteInLeaseAsync<int>(key, _ => Task.FromResult(42), CancellationToken.None);
        result.Should().Be(42, "semaphore must be released by finally block even when callback throws");

        var semaphores = NonPublicSpy.GetFieldValue<InMemoryStorage, ConcurrentDictionary<QueueKey, SemaphoreSlim>>(
            "_semaphores",
            sut
        );
        semaphores[key].CurrentCount.Should().Be(1, "semaphore count must return to 1 after cleanup");
    }

    // ---- INMEM-01: non-generic overload completes and releases ----

    [Fact]
    public async Task ExecuteInLeaseAsync_NonGenericOverload_ShouldCompleteAndReleaseSemaphore()
    {
        var sut = CreateSut();
        var key = NewKey();
        var callbackInvoked = false;

        await sut.ExecuteInLeaseAsync(
            key,
            ct =>
            {
                callbackInvoked = true;
                return Task.CompletedTask;
            },
            CancellationToken.None
        );

        callbackInvoked.Should().BeTrue("non-generic overload must invoke the callback");

        var semaphores = NonPublicSpy.GetFieldValue<InMemoryStorage, ConcurrentDictionary<QueueKey, SemaphoreSlim>>(
            "_semaphores",
            sut
        );
        semaphores[key].CurrentCount.Should().Be(1, "semaphore must be released after non-generic overload completes");
    }

    // ---- deadlock regression: GetDueSchedulesAsync / UpdateSchedulesAsync must not re-acquire scheduler semaphore ----

    [Fact]
    public async Task ExecuteInLeaseAsync_WhenCallbackCallsGetDueSchedulesAsync_ShouldNotDeadlock()
    {
        var clock = Substitute.For<IAtomizerClock>();
        var now = DateTimeOffset.UtcNow;
        clock.UtcNow.Returns(now);
        clock.MinValue.Returns(DateTimeOffset.MinValue);
        clock.MaxValue.Returns(DateTimeOffset.MaxValue);
        var logger = Substitute.For<TestableLogger<InMemoryStorage>>();
        var options = new InMemoryJobStorageOptions { AmountOfJobsToRetainInMemory = 100 };
        var sut = new InMemoryStorage(options, clock, logger);

        var schedule = AtomizerSchedule.Create(
            new JobKey("deadlock-get"),
            QueueKey.Default,
            typeof(string),
            "payload",
            Schedule.Default,
            TimeZoneInfo.Utc,
            now.AddMinutes(-1)
        );
        await sut.UpsertScheduleAsync(schedule, CancellationToken.None);

        IReadOnlyList<AtomizerSchedule> result = null!;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ExecuteInLeaseAsync(
            QueueKey.Scheduler,
            async ct =>
            {
                result = await sut.GetDueSchedulesAsync(now, ct);
            },
            cts.Token
        );

        result
            .Should()
            .ContainSingle("GetDueSchedulesAsync must complete inside the lease callback without deadlocking");
    }

    [Fact]
    public async Task ExecuteInLeaseAsync_WhenCallbackCallsUpdateSchedulesAsync_ShouldNotDeadlock()
    {
        var clock = Substitute.For<IAtomizerClock>();
        var now = DateTimeOffset.UtcNow;
        clock.UtcNow.Returns(now);
        clock.MinValue.Returns(DateTimeOffset.MinValue);
        clock.MaxValue.Returns(DateTimeOffset.MaxValue);
        var logger = Substitute.For<TestableLogger<InMemoryStorage>>();
        var options = new InMemoryJobStorageOptions { AmountOfJobsToRetainInMemory = 100 };
        var sut = new InMemoryStorage(options, clock, logger);

        var schedule = AtomizerSchedule.Create(
            new JobKey("deadlock-update"),
            QueueKey.Default,
            typeof(string),
            "payload",
            Schedule.Default,
            TimeZoneInfo.Utc,
            now
        );
        await sut.UpsertScheduleAsync(schedule, CancellationToken.None);
        schedule.Enabled = false;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ExecuteInLeaseAsync(
            QueueKey.Scheduler,
            async ct => await sut.UpdateSchedulesAsync([schedule], ct),
            cts.Token
        );

        var schedules = NonPublicSpy.GetFieldValue<InMemoryStorage, Dictionary<JobKey, AtomizerSchedule>>(
            "_schedules",
            sut
        );
        schedules[schedule.JobKey]
            .Enabled.Should()
            .BeFalse("UpdateSchedulesAsync must complete inside the lease callback without deadlocking");
    }

    // ---- INMEM-03: UpsertScheduleAsync and ExecuteInLeaseAsync(QueueKey.Scheduler) share the same semaphore (Design A) ----

    [Fact]
    public async Task UpsertScheduleAsync_WhenSchedulerLeaseHeld_ShouldBlockUntilLeaseReleased()
    {
        var clock = Substitute.For<IAtomizerClock>();
        var now = DateTimeOffset.UtcNow;
        clock.UtcNow.Returns(now);
        clock.MinValue.Returns(DateTimeOffset.MinValue);
        clock.MaxValue.Returns(DateTimeOffset.MaxValue);
        var logger = Substitute.For<TestableLogger<InMemoryStorage>>();
        var options = new InMemoryJobStorageOptions { AmountOfJobsToRetainInMemory = 100 };
        var sut = new InMemoryStorage(options, clock, logger);

        var leaseStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var leaseRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Hold the scheduler lease
        var leaseTask = sut.ExecuteInLeaseAsync(
            QueueKey.Scheduler,
            async ct =>
            {
                leaseStarted.SetResult(true);
                await leaseRelease.Task;
            },
            CancellationToken.None
        );

        await leaseStarted.Task;

        var schedule = AtomizerSchedule.Create(
            new JobKey("test-mutual-exclusion"),
            QueueKey.Default,
            typeof(string),
            "payload",
            Schedule.Default,
            TimeZoneInfo.Utc,
            now
        );

        // UpsertScheduleAsync should block while the lease is held
        var upsertTask = sut.UpsertScheduleAsync(schedule, CancellationToken.None);

        // Give upsertTask a moment to reach WaitAsync — it must be blocked, not completed
        await Task.Delay(50, TestContext.Current.CancellationToken);
        upsertTask.IsCompleted.Should().BeFalse("UpsertScheduleAsync must block while scheduler lease is held");

        // Release the lease — upsert can now proceed
        leaseRelease.SetResult(true);
        await leaseTask;
        var id = await upsertTask;
        id.Should().Be(schedule.Id, "UpsertScheduleAsync must complete after the scheduler lease is released");
    }
}
