---
phase: 05-cleanup-and-versioning
reviewed: 2026-05-03T00:00:00Z
depth: standard
files_reviewed: 44
files_reviewed_list:
  - src/Atomizer.EntityFrameworkCore/Atomizer.EntityFrameworkCore.csproj
  - src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs
  - src/Atomizer.EntityFrameworkCore/Extensions/ModelBuilderExtensions.cs
  - src/Atomizer.EntityFrameworkCore/Providers/DatabaseProvider.cs
  - src/Atomizer.EntityFrameworkCore/Providers/EntityMap.cs
  - src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScope.cs
  - src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreJobStorageOptions.cs
  - src/Atomizer/Abstractions/IAtomizerClient.cs
  - src/Atomizer/Abstractions/IAtomizerJob.cs
  - src/Atomizer/Abstractions/IAtomizerJobSerializer.cs
  - src/Atomizer/Abstractions/IAtomizerServiceScope.cs
  - src/Atomizer/Abstractions/IAtomizerStorage.cs
  - src/Atomizer/Atomizer.csproj
  - src/Atomizer/Configuration/AtomizerOptions.cs
  - src/Atomizer/Configuration/AtomizerOptionsExtensions.cs
  - src/Atomizer/Configuration/AtomizerProcessingOptions.cs
  - src/Atomizer/Configuration/JobStorageOptions.cs
  - src/Atomizer/Configuration/QueueOptions.cs
  - src/Atomizer/Configuration/SchedulingOptions.cs
  - src/Atomizer/Configuration/ServiceCollectionExtensions.cs
  - src/Atomizer/Core/AtomizerClient.cs
  - src/Atomizer/Core/AtomizerClock.cs
  - src/Atomizer/Core/AtomizerRuntimeIdentity.cs
  - src/Atomizer/Core/DefaultJobDispatcher.cs
  - src/Atomizer/Core/DefaultJobTypeResolver.cs
  - src/Atomizer/Exceptions/InvalidAtomizerConfigurationException.cs
  - src/Atomizer/Exceptions/InvalidJobKeyException.cs
  - src/Atomizer/Exceptions/InvalidLeaseTokenException.cs
  - src/Atomizer/Exceptions/InvalidQueueKeyException.cs
  - src/Atomizer/Exceptions/InvalidRetryStrategyException.cs
  - src/Atomizer/Exceptions/JobResolverException.cs
  - src/Atomizer/Exceptions/PayloadSerializationException.cs
  - src/Atomizer/Models/AtomizerJob.cs
  - src/Atomizer/Models/AtomizerJobError.cs
  - src/Atomizer/Models/AtomizerSchedule.cs
  - src/Atomizer/Models/Base/Model.cs
  - src/Atomizer/Models/Base/ValueObject.cs
  - src/Atomizer/Models/ValueObjects/JobKey.cs
  - src/Atomizer/Models/ValueObjects/LeaseToken.cs
  - src/Atomizer/Models/ValueObjects/QueueKey.cs
  - src/Atomizer/Models/ValueObjects/RetryStrategy.cs
  - src/Atomizer/Models/ValueObjects/Schedule.cs
  - src/Atomizer/Storage/InMemoryJobStorageOptions.cs
  - src/Atomizer/Storage/InMemoryStorage.cs
findings:
  critical: 3
  warning: 8
  info: 4
  total: 15
status: fixed
fixed_at: 2026-05-03T00:00:00Z
---

# Phase 05: Code Review Report

**Reviewed:** 2026-05-03T00:00:00Z
**Depth:** standard
**Files Reviewed:** 44
**Status:** issues_found

## Summary

This review covers the full public and internal API layer: models, value objects, storage, configuration, dispatcher, and EF Core extensions. The code is generally well-structured with good separation of concerns. Three correctness issues rise to BLOCKER level — an index-out-of-bounds crash in `RetryStrategy.GetRetryInterval` for the `None` strategy, broken `Schedule.Weekly` semantics (identical expression to `Daily`), and dead defensive branches in `LeaseToken` that follow a proven-length check. Eight warnings address thread safety, silent data loss, and misplaced XML docs. Four info items flag dead code, inconsistent validation, and a public setter leaking internal state.

---

## Critical Issues

### CR-01: `RetryStrategy.GetRetryInterval` throws `IndexOutOfRangeException` for `None` strategy

**File:** `src/Atomizer/Models/ValueObjects/RetryStrategy.cs:160-168`

**Issue:** `RetryStrategy.None` sets `MaxAttempts = 1` and `RetryIntervals = []` (empty array). `GetRetryInterval(1)` passes the bounds check (`1 >= 1 && 1 <= 1`) and then executes `RetryIntervals[0]` on an empty array, throwing `IndexOutOfRangeException` at runtime. Any caller that uses `None` as a retry strategy and then asks for the interval before the single attempt will crash. The `Fixed` factory allocates `maxAttempts` intervals, so `GetRetryInterval` works correctly for all `Fixed`/`Exponential`/`Intervals` strategies — but `None` is a special-cased singleton with the invariant violated.

**Fix:**
```csharp
// Option A: give None a zero-length interval consistent with its semantics
public static RetryStrategy None => new() { MaxAttempts = 1, RetryIntervals = [TimeSpan.Zero] };

// Option B: guard in GetRetryInterval
public TimeSpan GetRetryInterval(int attempt)
{
    if (attempt < 1 || attempt > MaxAttempts)
        throw new ArgumentOutOfRangeException(nameof(attempt), $"Attempt must be between 1 and {MaxAttempts}.");

    if (RetryIntervals.Length == 0)
        return TimeSpan.Zero;

    return RetryIntervals[attempt - 1];
}
```

---

### CR-02: `Schedule.Weekly` is identical to `Schedule.Daily` — wrong cron expression

**File:** `src/Atomizer/Models/ValueObjects/Schedule.cs:78`

**Issue:** `Schedule.Weekly` is defined as `new Schedule("0", "0", "0", "*", "*", "*")`, which fires at midnight every day — the same expression as `Schedule.Daily`. A correct weekly schedule needs a specific day-of-week constraint, e.g. Sunday (`"0"`). As currently defined, any consumer using `Schedule.Weekly` gets a daily schedule silently, causing jobs to run 7× more often than expected.

**Fix:**
```csharp
// Fires at midnight UTC every Sunday (day-of-week = 0)
public static Schedule Weekly => new Schedule("0", "0", "0", "*", "*", "0");
```
If "every Monday" is preferred, use `"1"` for the day-of-week field. The current `Schedule.Monthly` uses `"?"` for day-of-week, which is the Cronos wildcard for "unspecified" — consider aligning to `"1"` for the first of the month (`DayOfMonth = "1"`) to match its name and docs.

---

### CR-03: `new Random()` per call in `ApplyJitter` — non-thread-safe and produces correlated values under parallel execution

**File:** `src/Atomizer/Models/ValueObjects/RetryStrategy.cs:185`

**Issue:** `ApplyJitter` constructs `new Random()` on every invocation. In .NET Framework and .NET < 6 this seeds from `Environment.TickCount`, so two calls within the same millisecond produce identical jitter values — defeating the purpose entirely. Under parallel processing (which Atomizer explicitly supports via `DegreeOfParallelism`) multiple workers can call `Fixed(...)` concurrently and all receive the same interval, eliminating jitter-based spread and causing thundering-herd retry storms against storage. Even on .NET 6+ where `new Random()` uses a random seed, constructing a new instance per call is wasteful and semantically wrong.

**Fix:**
```csharp
// Use the thread-safe static Random.Shared (available .NET 6+ / net6.0 targets)
// For netstandard2.0 compatibility, use a static ThreadLocal<Random>
#if NET6_0_OR_GREATER
private static TimeSpan ApplyJitter(TimeSpan interval)
{
    var jitterFactor = 0.8 + Random.Shared.NextDouble() * 0.4;
    return TimeSpan.FromMilliseconds(interval.TotalMilliseconds * jitterFactor);
}
#else
private static readonly ThreadLocal<Random> _random =
    new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));

private static TimeSpan ApplyJitter(TimeSpan interval)
{
    var jitterFactor = 0.8 + _random.Value!.NextDouble() * 0.4;
    return TimeSpan.FromMilliseconds(interval.TotalMilliseconds * jitterFactor);
}
#endif
```

---

## Warnings

### WR-01: `InMemoryStorage.GetDueJobsAsync` reads `_jobs[id]` without a safe TryGetValue — silent KeyNotFoundException possible

**File:** `src/Atomizer/Storage/InMemoryStorage.cs:106`

**Issue:** The comment says "safe: ids derived under the same lock" but `GetDueJobsAsync` is not called inside any lock. A job id can be present in `_queues` but concurrently removed from `_jobs` by `EvictCompletedAndFailed` (which also runs without a lock) between the `ids.Keys` snapshot and the `_jobs[id]` indexer access. This produces a `KeyNotFoundException` crash in the poller task. The eviction method removes from `_queues` via `UnindexFromQueue` before removing from `_jobs`, but since there is no lock over both collections simultaneously, this ordering does not eliminate the race.

**Fix:**
```csharp
candidates = ids.Keys
    .Select(id => _jobs.TryGetValue(id, out var j) ? j : null)
    .Where(j => j != null && (
        (j.Status == AtomizerJobStatus.Pending
            && (j.VisibleAt == null || j.VisibleAt <= now)
            && j.ScheduledAt <= now)
        || (j.Status == AtomizerJobStatus.Processing && j.VisibleAt <= now)
    ))
    // ... rest unchanged
```

---

### WR-02: `InMemoryStorage.UpdateJobsAsync` silently drops updates for missing jobs — no idempotency key check

**File:** `src/Atomizer/Storage/InMemoryStorage.cs:66-75`

**Issue:** When a job is not found in `_jobs` (e.g. evicted between polling and processing), the method logs an error and continues. The job state change (completion, failure, retry schedule) is silently discarded. Callers have no way to detect this; the job's status never advances in storage. This causes ghost jobs that stall their retry counters and may cause the same job to be re-leased repeatedly after a visibility timeout expires — the in-memory equivalent of a stuck job.

**Fix:** Return the count of successfully updated jobs (or change the return type to `Task<int>`) and propagate the miss so callers can decide whether to abort or log at a higher severity. At minimum, elevate the log from `LogError` to a thrown exception:
```csharp
throw new InvalidOperationException($"Update requested for job {job.Id} that no longer exists in storage.");
```

---

### WR-03: `InMemoryStorage.UpdateSchedulesAsync` accesses `_schedules` dictionary without synchronization

**File:** `src/Atomizer/Storage/InMemoryStorage.cs:192-213`

**Issue:** `_schedules` is a plain `Dictionary<JobKey, AtomizerSchedule>` (not `ConcurrentDictionary`). `UpdateSchedulesAsync` reads and writes it without holding the per-queue semaphore or any lock. `UpsertScheduleAsync` protects its writes with `scheduleLock` (the `QueueKey.Scheduler` semaphore), but `UpdateSchedulesAsync` and `GetDueSchedulesAsync` do not acquire that same semaphore before accessing `_schedules`. Concurrent calls can corrupt the dictionary internals.

**Fix:** Acquire `_semaphores.GetOrAdd(QueueKey.Scheduler, ...)` before accessing `_schedules` in both `UpdateSchedulesAsync` and `GetDueSchedulesAsync`:
```csharp
public async Task UpdateSchedulesAsync(IEnumerable<AtomizerSchedule> schedules, CancellationToken cancellationToken)
{
    var scheduleLock = _semaphores.GetOrAdd(QueueKey.Scheduler, _ => new SemaphoreSlim(1, 1));
    await scheduleLock.WaitAsync(cancellationToken);
    try
    {
        // existing body
    }
    finally { scheduleLock.Release(); }
}
```

---

### WR-04: `LeaseToken` constructor contains dead branches after a proven-length guard

**File:** `src/Atomizer/Models/ValueObjects/LeaseToken.cs:52-54`

**Issue:** After the guard `if (parts.Length != 3) throw ...`, the code assigns parts using ternary conditions that re-check `parts.Length > 0`, `parts.Length > 1`, `parts.Length > 2`. These conditions are always `true` at that point (length has been proven to be exactly 3), so the else branches are unreachable dead code. More critically, the fallback values (`string.Empty`, `QueueKey.Default`) would produce a silently malformed `LeaseToken` if the guard were ever removed or weakened — hiding the true parse failure.

**Fix:**
```csharp
// After the guard, use direct indexing — the length is proven:
InstanceId = parts[0];
QueueKey = new QueueKey(parts[1]);
LeaseId = parts[2];
```

---

### WR-05: `AtomizerSchedule.GetOccurrences` with `MisfirePolicy.CatchUp` uses `LastEnqueueAt ?? CreatedAt` as the range start — can duplicate the boundary occurrence

**File:** `src/Atomizer/Models/AtomizerSchedule.cs:158-163`

**Issue:** `CronExpression.GetOccurrences(from, to, ...)` in Cronos is inclusive on `from`. If `LastEnqueueAt` equals the last occurrence that was already enqueued, that occurrence is included again in the next poll, producing a duplicate job. The idempotency key `{JobKey}:*:{occurrence:O}` would deduplicate the insertion in EF Core storage, but `InMemoryStorage.InsertAsync` performs no idempotency check — it overwrites whatever is at `job.Id` (which is a new GUID on each call), meaning duplicates are silently inserted.

**Fix:** Use an exclusive `from` by advancing one tick or using `GetOccurrences` with exclusive semantics:
```csharp
var from = (LastEnqueueAt ?? CreatedAt).AddTicks(1);
occurrences.AddRange(
    CronExpression.GetOccurrences(from, now, TimeZone)
        .OrderBy(dt => dt)
        .Take(MaxCatchUp)
);
```
Also, `InMemoryStorage.InsertAsync` should check `IdempotencyKey` before inserting to match EF Core storage semantics.

---

### WR-06: `EntityFrameworkCoreJobStorageOptions` XML `<remarks>` is placed inside `<summary>` — malformed documentation

**File:** `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreJobStorageOptions.cs:9-12`

**Issue:** The `<remarks>` tag for `AllowUnsafeProviderFallback` appears nested inside the `<summary>` block (lines 10-12 of the source show the `<remarks>` on the line after the `<summary>` text, without closing `</summary>` first). This produces malformed XML doc comments; tools like IntelliSense and `dotnet doc` will either ignore the remarks or render garbled output. The project has `GenerateDocumentationFile=true` and `TreatWarningsAsErrors=true`, but XML doc warnings are emitted as CS1570/CS1572 which may be suppressed.

**Fix:**
```csharp
/// <summary>
/// If true, allows falling back to providers that may not be
/// fully supported, tested or work in distributed environments (e.g. SQLite).
/// </summary>
/// <remarks>Default is false. See documentation for details and implications.</remarks>
public bool AllowUnsafeProviderFallback { get; set; } = false;
```

---

### WR-07: `AtomizerOptions.AddQueue` accepts `string name` instead of `QueueKey` — bypasses QueueKey validation in the public API

**File:** `src/Atomizer/Configuration/AtomizerOptions.cs:28`

**Issue:** The public `AddQueue` overload takes a raw `string name` and constructs `new QueueOptions(name)` which in turn calls the `QueueOptions(QueueKey queueKey)` constructor via implicit conversion. The implicit `string → QueueKey` conversion triggers validation (length ≤ 100, non-empty), which is correct. However, the `QueueOptions` constructor parameter is declared as `QueueKey` on line 47 of `QueueOptions.cs`, yet `AddQueue` is declared with `string name` on line 28 of `AtomizerOptions.cs` — the implicit conversion means the API surface advertises `string` while the validated type is `QueueKey`. Callers passing an empty or too-long string will get `InvalidQueueKeyException` from deep inside the conversion, not at the point of the `AddQueue` call. This makes the error context confusing for consumers.

**Fix:** Change the `AddQueue` signature to accept `QueueKey name` directly, making the validation boundary explicit:
```csharp
public AtomizerOptions AddQueue(QueueKey name, Action<QueueOptions>? configure = null)
```

---

### WR-08: `SchedulingOptions.TickInterval` exposes `internal set` on a public class — leaks internal-only state mutation to assembly-level callers

**File:** `src/Atomizer/Configuration/SchedulingOptions.cs:30`

**Issue:** `TickInterval` has `internal set`, making it mutable by any code in the `Atomizer` assembly but also discoverable via reflection from any consumer. `QueueOptions.TickInterval` correctly uses `private set`. The inconsistency means test code or integration glue in the same assembly can silently override the tick interval on `SchedulingOptions` while it appears immutable externally. It also leaks an implementation detail through the public API surface.

**Fix:**
```csharp
public TimeSpan TickInterval { get; private set; } = TimeSpan.FromSeconds(1);
```
If internal configuration is genuinely needed, document why or move it to an internal type.

---

## Info

### IN-01: `RetryStrategy.Intervals` has a dead null-check on a non-nullable value after `.ToArray()`

**File:** `src/Atomizer/Models/ValueObjects/RetryStrategy.cs:71`

**Issue:** `intervalsArray` is the result of calling `.ToArray()` on the `intervals` parameter (which is typed `IEnumerable<TimeSpan>` — a non-nullable value type enumerable). The result of `.ToArray()` is never null. The `intervalsArray is null` branch on line 71 is unreachable. It only causes the null check to be skipped by the compiler, adding noise.

**Fix:**
```csharp
if (intervalsArray.Length == 0)
    throw new InvalidRetryStrategyException("Intervals cannot be null or empty.", nameof(intervals));
```

---

### IN-02: `JobContext.Job` uses `null!` initializer — undetectable null at runtime for consumers

**File:** `src/Atomizer/Abstractions/IAtomizerJob.cs:27`

**Issue:** `public AtomizerJob Job { get; set; } = null!;` suppresses the nullable warning but does not enforce that `Job` is set before use. Any `IAtomizerJob<TPayload>` implementation accessing `context.Job` before the dispatcher sets it would get a `NullReferenceException` at runtime with no compile-time signal. The pattern is acceptable when the class is only ever constructed internally by the dispatcher, but since `JobContext` is a public `sealed class` in the consumer-facing namespace, consumer-written tests that construct `JobContext` manually will silently get a null `Job`.

**Fix:** Add a constructor that requires the job:
```csharp
public sealed class JobContext
{
    public JobContext(AtomizerJob job, CancellationToken cancellationToken = default)
    {
        Job = job;
        CancellationToken = cancellationToken;
    }

    public AtomizerJob Job { get; }
    public CancellationToken CancellationToken { get; }
}
```

---

### IN-03: `Schedule.Monthly` uses `"?"` for day-of-week but does not constrain `DayOfMonth`

**File:** `src/Atomizer/Models/ValueObjects/Schedule.cs:83`

**Issue:** `Schedule.Monthly` is `new Schedule("0", "0", "0", "*", "*", "?")`. The `DayOfMonth` field is `"*"` (every day) and the `DayOfWeek` is `"?"` (unspecified). With Cronos and `CronFormat.IncludeSeconds`, this fires at midnight on every day of every month — identical behaviour to `Schedule.Daily`. The intent was presumably "once per month on the 1st" (`DayOfMonth = "1"`). The bug means consumers using `Schedule.Monthly` get a daily job silently.

**Fix:**
```csharp
// Midnight UTC on the 1st of each month
public static Schedule Monthly => new Schedule("0", "0", "0", "1", "*", "*");
```

---

### IN-04: `AtomizerRuntimeIdentity` is not `sealed` despite being a concrete implementation class

**File:** `src/Atomizer/Core/AtomizerRuntimeIdentity.cs:6`

**Issue:** Per project conventions, all internal implementations use `internal sealed class`. `AtomizerRuntimeIdentity` is `public class` (not sealed). It has no `virtual` members and is not designed for inheritance. Not sealing it permits subclasses to override `InstanceId` (e.g. via a `new` property), which could introduce subtle identity bugs in lease token construction and error attribution across worker nodes.

**Fix:**
```csharp
public sealed class AtomizerRuntimeIdentity
```

---

_Reviewed: 2026-05-03T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
