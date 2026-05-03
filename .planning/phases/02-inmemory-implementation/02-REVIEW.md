---
phase: 02-inmemory-implementation
reviewed: 2026-05-03T00:00:00Z
depth: standard
files_reviewed: 4
files_reviewed_list:
  - src/Atomizer/Storage/InMemoryStorage.cs
  - src/Atomizer/Processing/QueuePoller.cs
  - tests/Atomizer.Tests/Storage/InMemoryStorageTests.cs
  - tests/Atomizer.Tests/Storage/InMemoryStorageLeaseTests.cs
findings:
  critical: 4
  warning: 5
  info: 3
  total: 12
status: issues_found
---

# Phase 02: Code Review Report

**Reviewed:** 2026-05-03T00:00:00Z
**Depth:** standard
**Files Reviewed:** 4
**Status:** issues_found

## Summary

The `InMemoryStorage` implementation and the refactored `QueuePoller` were reviewed. The core channel/lease mechanics in `QueuePoller` are sound. `InMemoryStorage` has several correctness and thread-safety defects that will cause data loss or incorrect behaviour in production. The most serious are: missing idempotency enforcement on `InsertAsync` (divergence from the EF Core backend contract), a data race on the plain `Dictionary<JobKey, AtomizerSchedule>` that is read without a lock, `ReleaseLeasedAsync` calling `job.Release()` on a mutable reference that is not persisted back to `_jobs`, and `UpdateLease` performing an unbounded linear scan over all lease sets when clearing a stale lease. Two test files also contain quality issues that reduce the reliability of the test suite.

---

## Critical Issues

### CR-01: `InsertAsync` silently discards idempotency — duplicate jobs are stored

**File:** `src/Atomizer/Storage/InMemoryStorage.cs:28-46`

**Issue:** `InsertAsync` never checks `job.IdempotencyKey`. It blindly writes `_jobs[job.Id] = job`, so two calls with the same `IdempotencyKey` but different `Id` values will both be stored. The EF Core backend explicitly queries for an existing job with the same key before inserting (see `EntityFrameworkCoreStorage.cs:38-50`). `ScheduleProcessor` relies on idempotency keys (`{JobKey}:*:{occurrence:O}`) to prevent duplicate schedule fan-out; without enforcement in `InMemoryStorage`, every recurring schedule will produce duplicate jobs on each poll tick that fires within the same occurrence window.

**Fix:**
```csharp
public Task<Guid> InsertAsync(AtomizerJob job, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();

    if (job.IdempotencyKey != null)
    {
        var existing = _jobs.Values.FirstOrDefault(j => j.IdempotencyKey == job.IdempotencyKey);
        if (existing != null)
        {
            _logger.LogDebug(
                "Job with idempotency key {IdempotencyKey} already exists with ID {JobId}",
                job.IdempotencyKey,
                existing.Id);
            return Task.FromResult(existing.Id);
        }
    }

    _jobs[job.Id] = job;
    IndexIntoQueue(job);
    // ... rest unchanged
}
```

---

### CR-02: `ReleaseLeasedAsync` mutates a stale object reference — released state is never persisted

**File:** `src/Atomizer/Storage/InMemoryStorage.cs:144`

**Issue:** `_jobs.TryGetValue(jobId, out var job)` retrieves the `AtomizerJob` reference that is currently stored in the dictionary. `job.Release(now)` calls the domain method which mutates the object in-place (sets `Status = Pending`, clears `LeaseToken`, clears `VisibleAt`). Because `AtomizerJob` is a reference type and the same reference is stored in `_jobs`, the mutation **does** propagate — for the current implementation.

However, `UpdateJobsAsync` at line 61 replaces the stored reference: `_jobs[job.Id] = job`. If the caller passes a **new copy** of the job (e.g., after deserialization or mapping), the reference in `_jobs` is replaced, and the next `TryGetValue` returns the new instance. After that point, `ReleaseLeasedAsync` mutates the stale old reference that is no longer in the dictionary, and the release is silently lost.

This is a latent data-loss bug. The fix is to always write the updated value back into the dictionary after mutation:

**Fix:**
```csharp
job.Release(now);
_jobs[jobId] = job;   // persist the mutation
released++;
```

---

### CR-03: `_schedules` is a plain `Dictionary` read without a lock — data race on concurrent readers

**File:** `src/Atomizer/Storage/InMemoryStorage.cs:14, 209-213`

**Issue:** `_schedules` is declared as `Dictionary<JobKey, AtomizerSchedule>` (not `ConcurrentDictionary`). `GetDueSchedulesAsync` at line 209 iterates `_schedules.Values` without acquiring any lock. `UpsertScheduleAsync` at line 167 writes to `_schedules` under the `QueueKey.Scheduler` semaphore, and `UpdateSchedulesAsync` at line 193 writes to it without any lock at all.

Concurrent calls to `GetDueSchedulesAsync` and `UpdateSchedulesAsync` (or a concurrent `GetDueSchedulesAsync` and `UpsertScheduleAsync`) will produce a data race: reading a `Dictionary` while another thread writes to it results in undefined behaviour in .NET, including thrown `InvalidOperationException` (collection modified during enumeration) or silently returning corrupt data.

**Fix:** Replace `_schedules` with `ConcurrentDictionary<JobKey, AtomizerSchedule>`, or take the scheduler semaphore in all read and write paths. The simpler fix:
```csharp
// Change field declaration
private readonly ConcurrentDictionary<JobKey, AtomizerSchedule> _schedules = new();

// GetDueSchedulesAsync: no lock needed — ConcurrentDictionary.Values snapshot is safe
// UpsertScheduleAsync: keep the scheduler semaphore for atomic read-modify-write
// UpdateSchedulesAsync: use TryUpdate or indexer — both are thread-safe on ConcurrentDictionary
```

---

### CR-04: `UpdateSchedulesAsync` writes to `_schedules` with no synchronisation

**File:** `src/Atomizer/Storage/InMemoryStorage.cs:177-198`

**Issue:** `UpdateSchedulesAsync` writes to `_schedules` at line 193 (`_schedules[schedule.JobKey] = schedule`) without holding the `QueueKey.Scheduler` semaphore. `UpsertScheduleAsync` holds the semaphore for its own writes, but that gives no protection against the unguarded writes in `UpdateSchedulesAsync`. Even after fixing CR-03 by switching to `ConcurrentDictionary`, a logical TOCTOU race remains: `UpdateSchedulesAsync` checks `ContainsKey` at line 186 and then writes at line 193, but without a lock another thread could remove the schedule between those two operations. For an in-memory store used during testing and low-volume use this is unlikely, but it contradicts the documented atomicity guarantee.

**Fix:** Acquire the scheduler semaphore for the full `UpdateSchedulesAsync` body, mirroring the pattern already used in `UpsertScheduleAsync`:
```csharp
public async Task UpdateSchedulesAsync(IEnumerable<AtomizerSchedule> schedules, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    var scheduleLock = _semaphores.GetOrAdd(QueueKey.Scheduler, _ => new SemaphoreSlim(1, 1));
    await scheduleLock.WaitAsync(cancellationToken);
    try
    {
        var now = _clock.UtcNow;
        foreach (var schedule in schedules)
        {
            if (!_schedules.ContainsKey(schedule.JobKey))
            {
                _logger.LogDebug("UpdateSchedules: schedule for jobKey={JobKey} not found", schedule.JobKey);
                continue;
            }
            schedule.UpdatedAt = now;
            _schedules[schedule.JobKey] = schedule;
        }
    }
    finally
    {
        scheduleLock.Release();
    }
}
```

---

## Warnings

### WR-01: `UpdateLease` performs an O(n) linear scan over all lease sets to remove a stale job

**File:** `src/Atomizer/Storage/InMemoryStorage.cs:347-354`

**Issue:** When `job.LeaseToken == null`, the `else` branch iterates every value in `_leasesByToken` calling `TryRemove`. With many queues or many active leases this scan is unbounded. More importantly, this is called from `UpdateJobsAsync` on every batch update, so the cost is `O(active leases × batch size)` per poll tick. For a storage that claims O(1) indexed operations via `ConcurrentDictionary` this is a correctness-quality issue: a job that was previously leased stores the old lease token on the object before `UpdateJobsAsync` is called, so the old token is known and a targeted `TryRemove` is possible.

**Fix:** Track the previous lease token before updating the stored reference, and remove only the specific entry:
```csharp
private void UpdateLease(AtomizerJob job)
{
    if (job.LeaseToken != null)
    {
        var leaseSet = _leasesByToken.GetOrAdd(job.LeaseToken.Token, _ => new ConcurrentDictionary<Guid, byte>());
        leaseSet[job.Id] = 0;
    }
    else
    {
        // Only scan if we know this job had a lease token — retrieve old reference first
        if (_jobs.TryGetValue(job.Id, out var stored) && stored.LeaseToken != null)
        {
            if (_leasesByToken.TryGetValue(stored.LeaseToken.Token, out var set))
            {
                set.TryRemove(job.Id, out _);
                if (set.IsEmpty)
                    _leasesByToken.TryRemove(stored.LeaseToken.Token, out _);
            }
        }
    }
}
```

---

### WR-02: `GetDueJobsAsync` dereferences `_jobs[id]` without a null-check — throws `KeyNotFoundException`

**File:** `src/Atomizer/Storage/InMemoryStorage.cs:94`

**Issue:** `ids.Keys.Select(id => _jobs[id])` uses the dictionary indexer, which throws `KeyNotFoundException` if the ID is absent. The comment says "safe: ids derived under the same lock", but `_jobs` and `_queues` are mutated from multiple paths without coordinating a global lock. Specifically, `EvictCompletedAndFailed` calls `UnindexFromQueue` to remove the id from the queue set, then removes it from `_jobs` — but these two operations are not atomic. A concurrent `GetDueJobsAsync` could read the queue index after `UnindexFromQueue` removed the id, but before the `_jobs.TryRemove` runs, so the id is already gone from both maps. Conversely, if eviction removes from `_jobs` first (the opposite ordering), `GetDueJobsAsync` reads a stale queue id that is no longer in `_jobs` and throws.

**Fix:** Use `TryGetValue` and filter out missing entries:
```csharp
candidates = ids.Keys
    .Select(id => { _jobs.TryGetValue(id, out var j); return j; })
    .Where(j => j != null && (
        (j.Status == AtomizerJobStatus.Pending && ...) || ...
    ))
    ...
```

---

### WR-03: `ExecuteInLeaseAsync` returns `default!` on a non-acquired lease — callers cannot distinguish "no jobs" from "lock not acquired"

**File:** `src/Atomizer/Storage/InMemoryStorage.cs:248`

**Issue:** When the semaphore is not acquired, the method returns `default!` (which is `null` for reference types, `0` for `int`, etc.). `QueuePoller` at line 95 works around this with `?? []`, but the contract documented on `IAtomizerStorage.ExecuteInLeaseAsync` says "the backend acquires its lock before invoking the callback and releases it after". There is no mention of a skip-and-return-default behaviour in the interface XML doc, meaning other callers (e.g., `SchedulePoller`) that do not know about this silent-skip may silently drop work. This also means a `TResult` of `int` is indistinguishable — a callback returning `0` looks identical to a skipped lease.

The interface contract should document the skip-and-return-default behaviour explicitly, and callers that cannot tolerate silent skips should detect it. Alternatively, return a discriminated result type.

**Fix (minimal — document the contract):** Add to `IAtomizerStorage.ExecuteInLeaseAsync<TResult>` XML doc:
```xml
/// <remarks>
/// If the backend cannot acquire the lease immediately (e.g., another poller holds it),
/// the callback is not invoked and <c>default</c> is returned.
/// Callers must treat <c>default</c> as "lease not acquired — no work done".
/// </remarks>
```
And update `QueuePoller` to use a wrapper type or a dedicated flag rather than relying on `?? []` heuristics.

---

### WR-04: `InsertAsync_WhenCalled_ShouldEvictOldJobs` test directly mutates `job.Status` bypassing domain methods

**File:** `tests/Atomizer.Tests/Storage/InMemoryStorageTests.cs:67`

**Issue:** `job.Status = AtomizerJobStatus.Completed;` directly sets the status property, bypassing `MarkAsCompleted(DateTimeOffset)`. This violates the project's domain method pattern ("State changes go through domain methods"). More critically, `MarkAsCompleted` sets `CompletedAt` and `UpdatedAt` which `EvictCompletedAndFailed` sorts by (`OrderByDescending(j => j.UpdatedAt)`). By skipping the domain method, `UpdatedAt` is left at its creation value and the eviction ordering is based on a wrong timestamp. The test may pass by accident but does not faithfully reflect production behaviour.

**Fix:**
```csharp
job.MarkAsCompleted(_now.AddMinutes(i));  // sets UpdatedAt correctly
```

---

### WR-05: `UpdateAsync_WhenJobMissing_ShouldLogError` verifies a non-structured log call that does not match the actual log call

**File:** `tests/Atomizer.Tests/Storage/InMemoryStorageTests.cs:125`

**Issue:** The assertion is:
```csharp
_logger.Received(1).LogError($"Update requested for missing job {job.Id}");
```
The production code logs:
```csharp
_logger.LogError("Update requested for missing job {JobId}", job.Id);
```
These are different: the production code uses a structured log template with a named parameter, while the test checks for the interpolated string. Depending on `TestableLogger`'s implementation, this assertion may never actually verify the structured log message, potentially always passing even if the log call is removed. The test gives false confidence.

**Fix:**
```csharp
_logger.Received(1).Log(
    LogLevel.Error,
    Arg.Any<EventId>(),
    Arg.Is<object>(s => s.ToString()!.Contains(job.Id.ToString())),
    null,
    Arg.Any<Func<object, Exception?, string>>()
);
```
Or use whatever `TestableLogger` provides for structured log verification.

---

## Info

### IN-01: `_semaphores` and `QueueKey.Scheduler` semaphore are duplicated between `InMemoryStorage` and the old `InMemoryLeasingScopeFactory`

**File:** `src/Atomizer/Storage/InMemoryStorage.cs:15`

**Issue:** After the storage refactor, `InMemoryStorage` now owns its own `_semaphores` `ConcurrentDictionary` (instance field). The CLAUDE.md architecture notes reference an `InMemoryLeasingScopeFactory` with a **static** `ConcurrentDictionary<QueueKey, (SemaphoreSlim, DateTimeOffset)>`. If the old factory still exists and is registered alongside the new storage, the two semaphore stores are independent and do not provide mutual exclusion against each other.

**Recommendation:** Confirm that `InMemoryLeasingScopeFactory` is fully replaced by `ExecuteInLeaseAsync` in this refactor and is no longer registered in DI. If it coexists, the locking guarantees are broken.

---

### IN-02: `QueuePoller` channel write error is silently swallowed — leased job is stranded

**File:** `src/Atomizer/Processing/QueuePoller.cs:116-124`

**Issue:** When `channel.Writer.WriteAsync` throws (line 114), the error is logged but the leased job is neither written to the channel nor explicitly released. The job remains in `Processing` status and will only become available again after its visibility timeout expires. The log message says "Will be retried after visibility timeout" which is accurate, but the exception that causes `WriteAsync` to throw on a bounded channel is `OperationCanceledException` when the token is cancelled — which is already caught at line 98. The only realistic non-cancellation exception is `ChannelClosedException` (channel completed). If that occurs, no further writes are possible and the outer loop should terminate. Silently continuing causes an infinite loop consuming CPU until cancellation.

**Recommendation:** Rethrow `ChannelClosedException` or break the loop:
```csharp
catch (ChannelClosedException)
{
    _logger.LogWarning("Channel closed for queue '{Queue}'. Stopping poller.", queue.QueueKey);
    return;
}
catch (Exception ex)
{
    _logger.LogError(ex, "Error writing leased job {JobId} ...", job.Id, queue.QueueKey);
}
```

---

### IN-03: `InMemoryStorage` is not XML-documented — violates project convention for public APIs

**File:** `src/Atomizer/Storage/InMemoryStorage.cs:8`

**Issue:** `InMemoryStorage` is `public sealed class` with no XML `<summary>` on the class or on any of its methods. The project convention (from CLAUDE.md) states "XML documentation required on all public APIs." The class implements `IAtomizerStorage` whose methods all have docs, but the concrete implementation has none. This will fail `EnablePackageValidation` or cause XML doc generation to produce incomplete output.

**Fix:** Add at minimum a class-level `<summary>`:
```csharp
/// <summary>
/// In-memory implementation of <see cref="IAtomizerStorage"/> backed by
/// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>.
/// Intended for development, testing, and single-node deployments.
/// </summary>
public sealed class InMemoryStorage : IAtomizerStorage
```

---

_Reviewed: 2026-05-03T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
