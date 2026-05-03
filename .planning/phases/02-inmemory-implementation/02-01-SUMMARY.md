---
phase: 02-inmemory-implementation
plan: "01"
subsystem: storage/inmemory
tags: [concurrency, semaphore, in-memory, leasing, thread-safety]
dependency_graph:
  requires: [01-leasing-abstraction]
  provides: [inmemory-execute-in-lease, inmemory-queue-index-thread-safety, upsert-schedule-atomic]
  affects: [QueuePoller, SchedulePoller, InMemoryStorage]
tech_stack:
  added: []
  patterns:
    - SemaphoreSlim(1,1) per-queue instance field with zero-timeout WaitAsync for lease acquire
    - ConcurrentDictionary<Guid,byte> as concurrent set (no ConcurrentHashSet in BCL)
    - Non-generic ExecuteInLeaseAsync delegates to generic overload (single lock path)
    - Design A unified semaphore: QueueKey.Scheduler entry in _semaphores IS the schedule lock
key_files:
  created: []
  modified:
    - src/Atomizer/Storage/InMemoryStorage.cs
    - src/Atomizer/Processing/QueuePoller.cs
    - tests/Atomizer.Tests/Storage/InMemoryStorageTests.cs
  deleted:
    - src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs
    - tests/Atomizer.Tests/Storage/InMemoryLeasingScopeFactoryTests.cs
decisions:
  - "Design A (unified semaphore): _semaphores[QueueKey.Scheduler] is the schedule lock; no separate _scheduleLock field needed — ensures UpsertScheduleAsync and ExecuteInLeaseAsync(QueueKey.Scheduler) are mutually exclusive"
  - "No empty-outer-key removal in UnindexFromQueue — avoids TOCTOU race between TryRemove and concurrent GetOrAdd"
  - "?? [] null-coalescing in QueuePoller rather than returning empty list from ExecuteInLeaseAsync — preserves the stated default! contract on the generic overload"
metrics:
  duration_seconds: 198
  completed_date: "2026-05-03"
  tasks_completed: 2
  tasks_total: 2
  files_changed: 5
---

# Phase 2 Plan 1: InMemory Leasing Implementation Summary

InMemoryStorage.ExecuteInLeaseAsync overloads fully implemented with per-queue SemaphoreSlim(1,1) instance field; _queues hardened with ConcurrentDictionary inner collection; UpsertScheduleAsync made atomic via unified scheduler semaphore; dead InMemoryLeasingScopeFactory deleted; QueuePoller null-coalescing guard added.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Implement ExecuteInLeaseAsync overloads, _semaphores field, collection thread-safety, delete dead file | 04dbea7 | InMemoryStorage.cs, InMemoryLeasingScopeFactory.cs (del), InMemoryLeasingScopeFactoryTests.cs (del), InMemoryStorageTests.cs |
| 2 | Add null-coalescing guard in QueuePoller.RunAsync | 1cbf4a8 | QueuePoller.cs |

## What Was Built

### InMemoryStorage: ExecuteInLeaseAsync overloads

Both `ExecuteInLeaseAsync<TResult>` (generic) and `ExecuteInLeaseAsync` (non-generic) are fully implemented.

The generic overload:
- Acquires the per-queue `SemaphoreSlim(1,1)` from `_semaphores` via `GetOrAdd` with `WaitAsync(TimeSpan.Zero, cancellationToken)` (zero-timeout, skip-on-busy)
- Returns `default!` when not acquired (caller must handle null for reference-type TResult)
- Invokes the callback inside a `try/finally` that always releases the semaphore
- Logs a debug message on skipped ticks

The non-generic overload delegates entirely to the generic overload (`ExecuteInLeaseAsync<bool>`) — single lock path, no duplication.

### InMemoryStorage: _semaphores field

`private readonly ConcurrentDictionary<QueueKey, SemaphoreSlim> _semaphores = new()` — instance field (not static), so each `InMemoryStorage` instance has fully isolated semaphores. This fixes the pre-existing deferred bug where `InMemoryLeasingScopeFactory.Semaphores` was a static dictionary causing test state bleed.

### InMemoryStorage: _queues thread-safety

Replaced `Dictionary<QueueKey, HashSet<Guid>> _queues` with `ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>> _queues`. The `ConcurrentDictionary<Guid, byte>` pattern serves as a concurrent set (standard BCL idiom — value is always `0`).

`IndexIntoQueue` and `UnindexFromQueue` helpers updated. `UnindexFromQueue` no longer removes the empty outer key — this is intentional to avoid a TOCTOU race between the remove-check and a concurrent `GetOrAdd` in `IndexIntoQueue`.

`GetDueJobsAsync` updated: `ids.Count == 0` → `ids.IsEmpty`, and `ids.Select(...)` → `ids.Keys.Select(...)`.

### InMemoryStorage: UpsertScheduleAsync atomic (Design A)

`UpsertScheduleAsync` now acquires `_semaphores[QueueKey.Scheduler]` via `GetOrAdd` with `WaitAsync(cancellationToken)` (always-wait, not zero-timeout — this is a client write call, not a skip-on-busy poll). All reads and writes to `_schedules` happen under this lock. Because `ExecuteInLeaseAsync(QueueKey.Scheduler, ...)` also uses `_semaphores[QueueKey.Scheduler]`, the two code paths are mutually exclusive — fixing the `@todo` race condition in the original `UpsertScheduleAsync` implementation.

### QueuePoller: null-coalescing guard

Added `?? []` after the `ExecuteInLeaseAsync` assignment. When the semaphore is not acquired, `ExecuteInLeaseAsync<List<AtomizerJob>>` returns `default!` (null). Without this guard, `leasedJobs.Count > 0` on the next line would throw `NullReferenceException` on every missed tick.

### Dead files deleted

- `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs` — no callers in `src/` after Phase 1 removed `IAtomizerLeasingScopeFactory`; deleted as cleanup
- `tests/Atomizer.Tests/Storage/InMemoryLeasingScopeFactoryTests.cs` — tests a deleted class; deleted per D-10

### Test update

`InMemoryStorageTests.InsertAsync_WhenCalled_ShouldStoreJobAndIndexQueue` updated to use `ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>>` field type and `.ContainKey()` assertion (was `Dictionary<QueueKey, HashSet<Guid>>` + `.Contain()`).

## Verification Results

```
NotImplementedException count in InMemoryStorage.cs: 0
InMemoryLeasingScopeFactory.cs: DELETED
_semaphores: private readonly (not static)
QueueKey.Scheduler in _semaphores: PRESENT
?? [] null guard in QueuePoller: 1 occurrence
dotnet build: 0 errors
dotnet test net8.0: 74/74 passed
dotnet test net10.0: 74/74 passed
```

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical Functionality] Updated test field-type assertion for _queues**
- **Found during:** Task 1
- **Issue:** `InMemoryStorageTests.InsertAsync_WhenCalled_ShouldStoreJobAndIndexQueue` used `Dictionary<QueueKey, HashSet<Guid>>` type assertion on `_queues` — would throw cast exception after the type change
- **Fix:** Updated `NonPublicSpy.GetFieldValue` generic type parameter and `.Contain()` to `.ContainKey()` to match the new `ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>>` type
- **Files modified:** `tests/Atomizer.Tests/Storage/InMemoryStorageTests.cs`
- **Commit:** 04dbea7

This was explicitly documented as Pitfall 4 in RESEARCH.md and was within plan scope (the plan listed `InMemoryStorageTests.cs` as a file that would need updating).

## Known Stubs

None. All stubs replaced with working implementations.

## Threat Flags

None. No new network endpoints, auth paths, file access, or trust boundary crossings introduced. The `_semaphores` instance field is in-process only and covered by T-02-01 (instance scope, not static) in the plan's threat model.

## Self-Check: PASSED

- FOUND: src/Atomizer/Storage/InMemoryStorage.cs
- FOUND: src/Atomizer/Processing/QueuePoller.cs
- FOUND: src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs DELETED (correct)
- FOUND: .planning/phases/02-inmemory-implementation/02-01-SUMMARY.md
- FOUND commit: 04dbea7
- FOUND commit: 1cbf4a8
