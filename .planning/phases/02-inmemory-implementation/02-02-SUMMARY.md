---
phase: 02-inmemory-implementation
plan: "02"
subsystem: storage/inmemory
tags: [concurrency, semaphore, in-memory, leasing, thread-safety, tests]
dependency_graph:
  requires: [02-01]
  provides: [inmemory-lease-tests, inmem-01-coverage, inmem-02-coverage, inmem-03-coverage]
  affects: [InMemoryStorageLeaseTests]
tech_stack:
  added: []
  patterns:
    - TaskCompletionSource for controlling async callback timing in concurrency tests
    - NonPublicSpy.GetFieldValue to assert SemaphoreSlim.CurrentCount via reflection
    - Task.Delay(50) guard to verify blocking behavior before releasing
key_files:
  created:
    - tests/Atomizer.Tests/Storage/InMemoryStorageLeaseTests.cs
  modified: []
  deleted: []
decisions:
  - "Task 1 (delete stale file + fix _queues assertion) was already completed by Wave 1 agent (02-01) — no duplicate work needed"
  - "Test 5 uses a separate CreateSut + clock inline to keep the mutual-exclusion test self-contained per the plan template"
metrics:
  duration_seconds: 420
  completed_date: "2026-05-03"
  tasks_completed: 2
  tasks_total: 2
  files_changed: 1
---

# Phase 2 Plan 2: InMemory Lease Tests Summary

Five lease-behavior tests added to InMemoryStorageLeaseTests.cs covering INMEM-01, INMEM-02, INMEM-03 via semaphore introspection and TaskCompletionSource-controlled concurrency; full suite green at 71 tests on net8.0 and net10.0.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Delete InMemoryLeasingScopeFactoryTests.cs and fix _queues assertion | (already done by 02-01: 04dbea7) | InMemoryLeasingScopeFactoryTests.cs (del), InMemoryStorageTests.cs |
| 2 | Create InMemoryStorageLeaseTests.cs with lease behavior coverage | 78bd717 | InMemoryStorageLeaseTests.cs |

## What Was Built

### InMemoryStorageLeaseTests.cs (new — 5 tests)

Five test methods covering the three requirements:

**INMEM-02: Semaphore held for full callback duration**
- `ExecuteInLeaseAsync_WhenCallbackCompletes_ShouldReleaseSemaphore` — verifies `_semaphores[key].CurrentCount == 1` after a successful call via `NonPublicSpy.GetFieldValue`

**INMEM-01/INMEM-02: Concurrent callers serialized; second skips**
- `ExecuteInLeaseAsync_WhenAlreadyAcquired_ShouldSkipCallbackAndReturnDefault` — holds the semaphore with a `TaskCompletionSource`-gated first caller; second concurrent caller gets `default(int)` and its callback is not invoked

**INMEM-02: Exception releases semaphore (D-04)**
- `ExecuteInLeaseAsync_WhenCallbackThrows_ShouldReleaseSemaphore` — throws inside callback; verifies subsequent call acquires and `CurrentCount == 1` afterward

**INMEM-01: Non-generic overload completes and releases**
- `ExecuteInLeaseAsync_NonGenericOverload_ShouldCompleteAndReleaseSemaphore` — verifies callback is invoked and semaphore is released

**INMEM-03: UpsertScheduleAsync and ExecuteInLeaseAsync(QueueKey.Scheduler) share semaphore**
- `UpsertScheduleAsync_WhenSchedulerLeaseHeld_ShouldBlockUntilLeaseReleased` — holds `ExecuteInLeaseAsync(QueueKey.Scheduler)` via `TaskCompletionSource`; fires `UpsertScheduleAsync` concurrently; asserts `upsertTask.IsCompleted == false` after 50ms delay; releases lease; asserts upsert completes with correct `Id`

### Task 1 status

Both sub-steps of Task 1 were already completed by the Wave 1 agent (02-01 commit `04dbea7`):
- `InMemoryLeasingScopeFactoryTests.cs` was already deleted
- `InMemoryStorageTests.cs` `_queues` assertion was already updated to `ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>>` with `.ContainKey`

## Verification Results

```
New test file exists:  EXISTS
Stale file deleted:    DELETED
Updated assertion:     1 occurrence (ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>>)
Lease tests (net8.0):  5/5 passed
Lease tests (net10.0): 5/5 passed
Full suite (net8.0):   71/71 passed
Full suite (net10.0):  71/71 passed
```

## Deviations from Plan

### Observations

**1. Task 1 already completed by Wave 1**
- **Found during:** Pre-execution file inspection
- **Action:** Skipped re-doing Task 1 entirely; confirmed artifacts were correct via file read and test run
- **Files verified:** InMemoryLeasingScopeFactoryTests.cs (deleted), InMemoryStorageTests.cs (assertion updated)
- **Commit:** 04dbea7 (Wave 1)

No bugs, missing functionality, or blocking issues were encountered. The implementation from 02-01 was complete and correct.

## Known Stubs

None. All 5 tests exercise real implementation paths.

## Threat Flags

None. Test-only file; no production code surface added. T-02-04 (NonPublicSpy reflection) is accepted per plan threat model.

## Self-Check: PASSED

- FOUND: tests/Atomizer.Tests/Storage/InMemoryStorageLeaseTests.cs
- FOUND commit: 78bd717
- VERIFIED: 5 lease tests pass on both target frameworks
- VERIFIED: 71 total tests pass (no regressions)
