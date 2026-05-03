---
phase: 01-leasing-abstraction
plan: "03"
subsystem: processing-pipeline
tags:
  - leasing
  - queue-poller
  - schedule-poller
  - refactor
dependency_graph:
  requires:
    - "01-01"  # ExecuteInLeaseAsync interface defined on IAtomizerStorage
  provides:
    - QueuePoller using ExecuteInLeaseAsync generic overload
    - SchedulePoller using ExecuteInLeaseAsync non-generic overload
  affects:
    - src/Atomizer/Processing/QueuePoller.cs
    - src/Atomizer/Scheduling/SchedulePoller.cs
tech_stack:
  added: []
  patterns:
    - Callback-based leasing (ExecuteInLeaseAsync replaces acquire/check/dispose pattern)
key_files:
  modified:
    - src/Atomizer/Processing/QueuePoller.cs
    - src/Atomizer/Scheduling/SchedulePoller.cs
decisions:
  - "Channel-write loop kept outside ExecuteInLeaseAsync callback — jobs are returned as List<AtomizerJob> from the generic overload and written to channel after the lease completes"
  - "SchedulePoller uses non-generic overload (Task, no return) — no leasedJobs collection needed for schedule processing"
  - "leasedJobs initialised with [] (empty collection initialiser) so channel-write guard leasedJobs.Count > 0 works when timing guard not met"
metrics:
  duration: ~5 min
  completed: "2026-05-03"
  tasks_completed: 2
  files_modified: 2
requirements:
  - LEASE-01
  - LEASE-03
---

# Phase 1 Plan 03: Migrate Pollers to ExecuteInLeaseAsync Summary

**One-liner:** Rewrote QueuePoller and SchedulePoller from the two-step acquire/check/dispose leasing pattern to the single `ExecuteInLeaseAsync` callback pattern, eliminating all `IAtomizerLeasingScopeFactory`, `IAtomizerLeasingScope`, and `leasingScope.Acquired` references from both files.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Rewrite QueuePoller to use ExecuteInLeaseAsync (generic overload) | cc554c5 | src/Atomizer/Processing/QueuePoller.cs |
| 2 | Rewrite SchedulePoller to use ExecuteInLeaseAsync (non-generic overload) | 9cd6cc7 | src/Atomizer/Scheduling/SchedulePoller.cs |

## What Changed

### QueuePoller.cs

- Replaced the `leasingScopeFactory.CreateScopeAsync` + `#if NETCOREAPP3_0_OR_GREATER` + `leasingScope.Acquired` block with a single call to `storage.ExecuteInLeaseAsync<List<AtomizerJob>>`.
- The callback receives `innerCt`, fetches due jobs, calls `job.Lease(...)` and `UpdateJobsAsync`, and returns the acquired list.
- `leasedJobs` is declared as `List<AtomizerJob> leasedJobs = []` before the try block and assigned from the return value of `ExecuteInLeaseAsync`. If the timing guard is not met, `leasedJobs` stays empty.
- The channel-write loop (`if (leasedJobs.Count > 0) { ... channel.Writer.WriteAsync ... }`) is preserved entirely outside the callback and outside the try/catch block — structural invariant upheld.
- Outer timing guard (`now - _lastStorageCheck >= storageCheckInterval && itemsInChannel < queue.DegreeOfParallelism`) is preserved.

### SchedulePoller.cs

- Replaced the `leasingScopeFactory.CreateScopeAsync` + `#if NETCOREAPP3_0_OR_GREATER` + `leasingScope.Acquired` block with `await storage.ExecuteInLeaseAsync(QueueKey.Scheduler, async innerCt => { ... }, execToken)`.
- Uses the non-generic overload (returns `Task`, no return value from callback).
- All schedule-processing logic (GetDueSchedulesAsync, foreach with PayloadType null check, ProcessAsync, UpdateNextOccurence, UpdateSchedulesAsync) is preserved inside the callback.
- `GetDueSchedulesAsync(horizon, ioToken)` correctly passes `ioToken` (not `innerCt`) as it did in the original — preserved exactly.
- Outer timing guard preserved intact.

## Deviations from Plan

None — plan executed exactly as written.

## Threat Surface Scan

No new network endpoints, auth paths, file access patterns, or schema changes introduced. Both files are internal processing pipeline components that delegate to `IAtomizerStorage`. No new threat surface.

## Known Stubs

None — both files are fully wired to the `ExecuteInLeaseAsync` interface. The underlying implementations (InMemoryStorage, EntityFrameworkCoreStorage) are stubbed/incomplete in other plans, but the call sites in this plan are complete.

## Build Notes

The full solution build (`dotnet build Atomizer.sln --no-restore`) shows errors in `InMemoryStorage.cs`, `InMemoryLeasingScopeFactory.cs`, and `AtomizerOptions.cs` — these are pre-existing failures from Plan 02 (parallel wave, different worktree) which is responsible for implementing `ExecuteInLeaseAsync` on InMemoryStorage and cleaning up the old leasing types. Neither `QueuePoller.cs` nor `SchedulePoller.cs` contributes any build errors.

## Self-Check

Verifying claims before marking complete.

| Check | Result |
|-------|--------|
| QueuePoller.cs exists | FOUND |
| SchedulePoller.cs exists | FOUND |
| 01-03-SUMMARY.md exists | FOUND |
| Commit cc554c5 (QueuePoller) | FOUND |
| Commit 9cd6cc7 (SchedulePoller) | FOUND |
| QueuePoller old leasing refs | 0 |
| SchedulePoller old leasing refs | 0 |
| QueuePoller ExecuteInLeaseAsync | 1 match |
| SchedulePoller ExecuteInLeaseAsync | 1 match |

## Self-Check: PASSED
