---
phase: 01-leasing-abstraction
reviewed: 2026-05-03T00:00:00Z
depth: standard
files_reviewed: 17
files_reviewed_list:
  - src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs
  - src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScope.cs
  - src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs
  - src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs
  - src/Atomizer/Abstractions/IAtomizerServiceScope.cs
  - src/Atomizer/Abstractions/IAtomizerStorage.cs
  - src/Atomizer/Configuration/AtomizerOptions.cs
  - src/Atomizer/Configuration/AtomizerOptionsExtensions.cs
  - src/Atomizer/Configuration/ServiceCollectionExtensions.cs
  - src/Atomizer/Core/ServiceProviderServiceScope.cs
  - src/Atomizer/Processing/QueuePoller.cs
  - src/Atomizer/Scheduling/SchedulePoller.cs
  - src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs
  - src/Atomizer/Storage/InMemoryStorage.cs
  - tests/Atomizer.EntityFrameworkCore.Tests/Storage/DatabaseTransactionLeasingScopeFactoryTests.cs
  - tests/Atomizer.Tests/Processing/QueuePollerTests.cs
  - tests/Atomizer.Tests/Scheduling/SchedulePollerTests.cs
findings:
  critical: 3
  warning: 6
  info: 3
  total: 12
status: issues_found
---

# Phase 01: Code Review Report

**Reviewed:** 2026-05-03T00:00:00Z
**Depth:** standard
**Files Reviewed:** 17
**Status:** issues_found

## Summary

Phase 01 introduces `ExecuteInLeaseAsync` as a callback-based leasing contract on `IAtomizerStorage`, rewrites `QueuePoller` and `SchedulePoller` to call it, and retains the old `InMemoryLeasingScopeFactory` / `DatabaseTransactionLeasingScope*` types as Phase-5 survivors. Three blockers require attention before this phase is considered correct: the `NotImplementedException` stubs on both storage backends make the entire processing pipeline non-functional at runtime; the singleton `QueuePoller` carries per-queue mutable state (`_lastStorageCheck`) that is shared and mutated concurrently across all queues; and `InMemoryLeasingScopeFactory.CreateScopeAsync` blocks the thread synchronously via `.Result` while awaiting an async operation. Six warnings cover structural and correctness issues of lesser severity.

---

## Critical Issues

### CR-01: `ExecuteInLeaseAsync` stubs make the processing pipeline non-functional at runtime

**File:** `src/Atomizer/Storage/InMemoryStorage.cs:221-238`
**Also:** `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:241-259`

**Issue:** Both `ExecuteInLeaseAsync<TResult>` and `ExecuteInLeaseAsync` are stubs that unconditionally throw `NotImplementedException`. `QueuePoller.RunAsync` calls the generic overload and `SchedulePoller.RunAsync` calls the void overload. Any deployment using either storage backend (which means every deployment) will throw on the first poll tick, causing the processing pipeline and scheduler to log an error and back off indefinitely. No jobs or schedules will ever be processed.

The `InMemoryStorage` comment says "TODO: Implemented in Phase 2" and the EF Core comment says "TODO: Implemented in Phase 4". If these phases have not yet landed, the pipeline is broken end-to-end in this branch today.

**Fix:** Implement both overloads before shipping this phase, or gate the `QueuePoller`/`SchedulePoller` rewrites behind the phase that actually delivers the implementations. A minimal correct in-memory implementation:

```csharp
// InMemoryStorage.cs
public async Task<TResult> ExecuteInLeaseAsync<TResult>(
    QueueKey queue,
    Func<CancellationToken, Task<TResult>> callback,
    CancellationToken cancellationToken)
{
    // InMemory: no distributed lock needed — just invoke the callback directly.
    // The per-queue SemaphoreSlim from InMemoryLeasingScopeFactory could be reused
    // here if exclusive access is required across concurrent in-process pollers.
    return await callback(cancellationToken);
}

public async Task ExecuteInLeaseAsync(
    QueueKey queue,
    Func<CancellationToken, Task> callback,
    CancellationToken cancellationToken)
{
    await callback(cancellationToken);
}
```

---

### CR-02: Singleton `QueuePoller` shares mutable `_lastStorageCheck` across all queues — race condition

**File:** `src/Atomizer/Processing/QueuePoller.cs:19`
**Also:** `src/Atomizer/Configuration/ServiceCollectionExtensions.cs:74`
**Also:** `src/Atomizer/Processing/QueuePump.cs` (via `QueuePumpFactory`)

**Issue:** `QueuePoller` is registered as a singleton (`services.AddSingleton<IQueuePoller, QueuePoller>()`). `QueuePumpFactory.Create` passes the same singleton `IQueuePoller` instance into every `QueuePump`. Each pump runs `_poller.RunAsync(...)` concurrently on its own task. The instance field `_lastStorageCheck` (line 19) is read and written unsynchronised from multiple concurrent tasks. The result is:

1. All queues share and stomp on each other's check timestamp — a queue polling at `queue-A`'s interval will suppress or prematurely trigger polls for `queue-B`.
2. `_lastStorageCheck` is a `DateTimeOffset` (16 bytes on 64-bit), which is not atomically writable, so a torn read is possible on 32-bit runtimes.

`SchedulePoller` has the same field (`src/Atomizer/Scheduling/SchedulePoller.cs:20`) but is only ever run from a single task, so it is safe in isolation.

**Fix:** Register `IQueuePoller` as transient so each pump gets its own instance:

```csharp
// ServiceCollectionExtensions.cs
services.AddTransient<IQueuePoller, QueuePoller>();
```

Alternatively, move `_lastStorageCheck` into a per-call local or per-`QueueOptions` state object if singleton lifetime is required for other reasons.

---

### CR-03: `InMemoryLeasingScopeFactory.CreateScopeAsync` blocks the thread synchronously via `.Result`

**File:** `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs:27-35`

**Issue:** `CreateScopeAsync` calls `InMemoryLeasingScope.AcquireAsync(...)` which returns a `Task<InMemoryLeasingScope>`, then immediately reads `.Result` on line 32 to log the `Acquired` state before returning the task. Reading `.Task.Result` while the task has not completed blocks the calling thread synchronously. Although `AcquireAsync` uses `WaitAsync(TimeSpan.Zero, ...)` which is typically fast, it is still an async operation dispatched through the TPL. Blocking on `.Result` in an async context can cause deadlocks under `SynchronizationContext`-bound callers (e.g., ASP.NET Framework, unit-test runners with a sync context) and starves thread-pool threads under load.

```csharp
// Line 27-35 — scope.Result.Acquired is the blocking read
public Task<InMemoryLeasingScope> CreateScopeAsync(...)
{
    var scope = InMemoryLeasingScope.AcquireAsync(key, scopeTimeout, _clock.UtcNow, cancellationToken);
    _logger.LogDebug("...", key, scope.Result.Acquired);   // <-- blocks here
    return scope;
}
```

**Fix:** Make the method properly async and await the task:

```csharp
public async Task<InMemoryLeasingScope> CreateScopeAsync(
    QueueKey key,
    TimeSpan scopeTimeout,
    CancellationToken cancellationToken)
{
    _logger.LogDebug("Acquiring in-memory leasing scope for queue {QueueKey}", key);
    var scope = await InMemoryLeasingScope.AcquireAsync(key, scopeTimeout, _clock.UtcNow, cancellationToken);
    _logger.LogDebug("In memory leasing scope for queue {QueueKey} acquired: {Acquired}", key, scope.Acquired);
    return scope;
}
```

---

## Warnings

### WR-01: `QueuePoller` channel-write is outside the `ExecuteInLeaseAsync` callback — jobs leased but never written to channel are silently lost

**File:** `src/Atomizer/Processing/QueuePoller.cs:102-126`

**Issue:** The `ExecuteInLeaseAsync` callback (lines 58-89) acquires the lease, calls `GetDueJobsAsync`, marks jobs as `Processing`, and calls `UpdateJobsAsync`. The jobs are committed as `Processing` inside the lease. Then, *after* the lease is released (the callback returns), the code loops over `leasedJobs` and writes each to the channel (lines 102-119). If `channel.Writer.WriteAsync` throws or blocks (bounded channel, shutdown), the already-leased jobs are stranded as `Processing` until their visibility timeout expires. This is by design according to the comment "Will be retried after visibility timeout", but there is an additional issue: if the cancellation token fires between the lease commit and the channel write loop, the `OperationCanceledException` from `WriteAsync` is caught and logged as an error (line 110), but the outer loop then exits at the `Task.Delay` catch — the remaining jobs in `leasedJobs` that have not yet been written are simply abandoned. They will not be written to the channel and will sit as `Processing` until the visibility timeout.

This means the window between lease-commit and channel-write is a correctness gap: jobs can be lost from the current pump's view for the duration of the visibility timeout, creating avoidable latency spikes.

**Fix:** Either move the channel-write inside the callback (before `UpdateJobsAsync` commits), or collect any un-written jobs after the loop and call `ReleaseLeasedAsync` on them immediately rather than waiting for the timeout.

---

### WR-02: `SchedulePoller` passes `execToken` into `_scheduleProcessor.ProcessAsync` but `execToken` is not cancelled when `ioToken` fires — schedule processor may continue after poller stops

**File:** `src/Atomizer/Scheduling/SchedulePoller.cs:72`

**Issue:** Inside the `ExecuteInLeaseAsync` callback, `_scheduleProcessor.ProcessAsync(schedule, horizon, execToken)` is invoked with the execution token. The execution token is only cancelled when the graceful shutdown deadline expires. However, the `ExecuteInLeaseAsync` call itself uses `ioToken` (line 79) as the outer cancellation token. When `ioToken` is cancelled (normal shutdown), the lease acquisition is cancelled, but any `ProcessAsync` call that is already in flight inside the callback will continue running because `execToken` is still live. This means schedule processors can enqueue jobs after the scheduler has nominally stopped. This matches the two-phase shutdown design, but the `UpdateSchedulesAsync` call (line 76) also runs with `innerCt` (derived from `ioToken`), so schedule state may not be persisted if `ioToken` is already cancelled by the time the callback returns. The result is that successfully processed schedules may have `NextRunAt` not advanced, causing duplicate execution on restart.

**Fix:** Ensure `UpdateSchedulesAsync` is called with a non-cancellable token (or a short dedicated timeout token) when it is part of commit-after-work logic:

```csharp
// After processing, persist with a short timeout rather than the already-cancelled ioToken
await storage.UpdateSchedulesAsync(dueSchedules, CancellationToken.None);
```

---

### WR-03: `AtomizerOptions.SchedulingOptions` has `internal set` but the test directly assigns it — breaks encapsulation and couples tests to internals

**File:** `src/Atomizer/Configuration/AtomizerOptions.cs:11`
**Also:** `tests/Atomizer.Tests/Scheduling/SchedulePollerTests.cs:30`

**Issue:** `SchedulingOptions` is declared `internal SchedulingOptions SchedulingOptions { get; set; }`. The test at line 30 directly assigns a custom `SchedulingOptions` instance to this property (`atomizerOptions.SchedulingOptions = options`). This works only because the test project has access via `InternalsVisibleTo` (or assembly-level access), but it is fragile: the `set` accessor is `internal`, so this assignment bypasses the intended configuration path (`ConfigureScheduling(Action<SchedulingOptions>)`). More importantly, `AtomizerOptions.ConfigureScheduling` performs validation (lines 75-90) that is completely skipped when the property is set directly. A test could supply a `SchedulingOptions` with `StorageCheckInterval = TimeSpan.Zero` without triggering the guard.

**Fix:** Remove the public setter from the test path. Instead, use `ConfigureScheduling` in test setup, or expose a test-only helper in `Atomizer.Tests.Utilities`. If direct assignment from tests is genuinely needed, add a constructor overload to `SchedulePoller` that takes `SchedulingOptions` directly (bypassing `AtomizerOptions`).

---

### WR-04: `UpsertScheduleAsync` log call passes `schedule.JobKey` for both `{ScheduleKey}` and `{JobKey}` placeholders

**File:** `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:183-185`

**Issue:** The structured log call uses two distinct placeholder names (`{ScheduleKey}` and `{JobKey}`) but passes the same argument (`schedule.JobKey`) for both:

```csharp
_logger.LogError(
    ex,
    "Failed to upsert schedule {ScheduleKey} for job {JobKey}",
    schedule.JobKey,   // fills {ScheduleKey}
    schedule.JobKey    // fills {JobKey} — duplicate, always identical
);
```

The second placeholder is meaningless. This looks like a copy-paste error where a separate schedule ID or name was intended for `{ScheduleKey}`.

**Fix:**
```csharp
_logger.LogError(
    ex,
    "Failed to upsert schedule for job {JobKey}",
    schedule.JobKey
);
```

---

### WR-05: `DatabaseTransactionLeasingScopeFactory` is no longer registered in DI but still exists as a public class — dead code with no clear future contract

**File:** `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs`
**Also:** `src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs`

**Issue:** `UseEntityFrameworkCoreStorage` registers only `EntityFrameworkCoreStorage<TDbContext>` as `IAtomizerStorage`. `DatabaseTransactionLeasingScopeFactory<TDbContext>` is no longer registered anywhere in DI (the old `IAtomizerLeasingScopeFactory` registration has been removed). The class is `public` (not `internal`) and carries no `[Obsolete]` attribute. Its `CreateScopeAsync` method throws `NotSupportedException` for non-relational providers rather than returning a non-acquired scope, which is inconsistent with how `DatabaseTransactionLeasingScope.StartTransaction` handles failures (returns `Acquired = false`). The class will be dead code unless explicitly instantiated manually.

**Fix:** Either mark the class `internal` since it is no longer part of the public API, or add `[Obsolete("Will be removed in Phase 5")]` to communicate intent. If it is kept as a Phase-5 survivor, document that clearly in a `<remarks>` XML doc tag. Additionally, the `NotSupportedException` throw path should be replaced with a non-acquired scope return to match the failure contract of `StartTransaction`:

```csharp
// Instead of throwing, return non-acquired scope
_logger.LogDebug("Database is not relational, returning non-acquired scope for queue {QueueKey}", key);
return Task.FromResult(new DatabaseTransactionLeasingScope(null));
```

---

### WR-06: `InMemoryLeasingScopeFactory` is an orphaned internal class — no longer wired or callable from any production code path

**File:** `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs`
**Also:** `src/Atomizer/Configuration/AtomizerOptionsExtensions.cs`

**Issue:** `UseInMemoryStorage` now registers `InMemoryStorage` directly; it does not register `InMemoryLeasingScopeFactory`. No other production code creates or references `InMemoryLeasingScopeFactory`. The class exists as dead code. Because it holds a `static` `ConcurrentDictionary<QueueKey, (SemaphoreSlim, DateTimeOffset)>` (`Semaphores`, line 9), this static state is live for the lifetime of the process even though no code path ever calls it — a silent resource concern if many test runs reuse the process (e.g., `dotnet test` with parallelism).

**Fix:** Either delete `InMemoryLeasingScopeFactory` entirely, or document it as a Phase-5 survivor with `internal sealed` access and a clear comment explaining when it will be re-wired.

---

## Info

### IN-01: `IAtomizerServiceScopeFactory` and `IAtomizerServiceScope` have no XML documentation

**File:** `src/Atomizer/Abstractions/IAtomizerServiceScope.cs:3-11`

**Issue:** Both `IAtomizerServiceScopeFactory` and `IAtomizerServiceScope` are `public` interfaces with no `<summary>`, `<returns>`, or `<param>` tags. Per the project's XML documentation requirement ("XML documentation required on all public APIs"), these need doc comments. The `Storage` property on `IAtomizerServiceScope` is particularly important to document because it is the sole access point to the storage backend within a scoped context.

**Fix:**
```csharp
/// <summary>
/// Creates <see cref="IAtomizerServiceScope"/> instances for resolving scoped services,
/// including the <see cref="IAtomizerStorage"/> implementation.
/// </summary>
public interface IAtomizerServiceScopeFactory
{
    /// <summary>Creates a new service scope.</summary>
    /// <returns>A new <see cref="IAtomizerServiceScope"/> that must be disposed after use.</returns>
    IAtomizerServiceScope CreateScope();
}

/// <summary>
/// Represents a scoped context providing access to Atomizer infrastructure services.
/// Dispose the scope when work is complete to release underlying resources.
/// </summary>
public interface IAtomizerServiceScope : IDisposable
{
    /// <summary>Gets the <see cref="IAtomizerStorage"/> instance for this scope.</summary>
    IAtomizerStorage Storage { get; }
}
```

---

### IN-02: `DatabaseTransactionLeasingScopeFactory` and `DatabaseTransactionLeasingScope` are `public` but have no XML documentation

**File:** `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs:6`
**Also:** `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScope.cs:10`

**Issue:** Both classes are `public` but carry no `<summary>`, `<param>`, or `<returns>` XML documentation on their public members. `DatabaseTransactionLeasingScope` has a `<summary>` on the class level (line 7) but none on its constructor, `Acquired` property, `Dispose`, `DisposeAsync`, or the static factory method `StartTransaction`. The project convention requires XML docs on all public APIs.

**Fix:** Add `<summary>`, `<param>`, and `<returns>` tags to all public members of both classes.

---

### IN-03: `DisposeAsync` in `InMemoryLeasingScope` performs a no-op await

**File:** `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs:99-103`

**Issue:** The `IAsyncDisposable.DisposeAsync` implementation calls the synchronous `Dispose()` then `await Task.CompletedTask`. `await Task.CompletedTask` does nothing and generates an unnecessary state machine allocation on older runtimes. This is harmless but misleading — it implies there is async work when there is none.

**Fix:**
```csharp
public ValueTask DisposeAsync()
{
    Dispose();
    return ValueTask.CompletedTask;
}
```

---

_Reviewed: 2026-05-03T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
