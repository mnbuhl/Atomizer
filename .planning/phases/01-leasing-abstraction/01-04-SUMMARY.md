---
phase: 01-leasing-abstraction
plan: "04"
subsystem: tests
tags:
  - test-cleanup
  - leasing-abstraction
  - nsubstitute
dependency_graph:
  requires:
    - "01-01"
    - "01-02"
    - "01-03"
  provides:
    - clean-test-suite-compile
    - passing-queue-poller-tests
    - passing-schedule-poller-tests
  affects:
    - tests/Atomizer.Tests/Processing/QueuePollerTests.cs
    - tests/Atomizer.Tests/Scheduling/SchedulePollerTests.cs
tech_stack:
  added: []
  patterns:
    - "NSubstitute constructor-level ExecuteInLeaseAsync mock for shared test setup"
    - "Generic Func<CancellationToken, Task<TResult>> overload matching for NSubstitute"
key_files:
  created: []
  modified:
    - tests/Atomizer.Tests/Processing/QueuePollerTests.cs
  deleted:
    - tests/Atomizer.Tests/Core/NoopLeasingScopeFactoryTests.cs
decisions:
  - "Constructor-level ExecuteInLeaseAsync mock covers all QueuePoller tests including cancellation-only test"
  - "Per-test mocks removed; constructor default satisfies all scenarios"
metrics:
  duration: "~10 minutes"
  completed: "2026-05-03"
  tasks_completed: 3
  files_modified: 1
  files_deleted: 1
requirements:
  - LEASE-01
  - LEASE-02
---

# Phase 01 Plan 04: Fix Test Compile After Leasing Abstraction Summary

Wave 2 (Plans 01-03) had already completed all three task file changes before this agent ran: `NoopLeasingScopeFactoryTests.cs` was deleted, `QueuePollerTests.cs` had leasing references removed, and `SchedulePollerTests.cs` was fully updated with the new `ExecuteInLeaseAsync` non-generic mock pattern. This plan's agent verified those prior changes were correct, then discovered and fixed a remaining bug: the per-test mocks in `QueuePollerTests.cs` used the **non-generic** `Func<CancellationToken, Task>` overload while `QueuePoller` calls the **generic** `ExecuteInLeaseAsync<List<AtomizerJob>>` overload — causing `NullReferenceException` at `leasedJobs.Count`.

## Tasks Completed

| Task | Name | Status | Commit |
|------|------|--------|--------|
| 1 | Delete NoopLeasingScopeFactoryTests.cs | Already done by Wave 2 | 036aa8a |
| 2 | Update QueuePollerTests — remove leasing mocks, wire ExecuteInLeaseAsync | Partially done; Rule 1 fix applied | 4e636b1 |
| 3 | Update SchedulePollerTests — remove leasing mocks, wire ExecuteInLeaseAsync | Already done by Wave 2 | 036aa8a |

## What Was Built

The test project now compiles and all 74 tests pass (net8.0, net10.0). The `ExecuteInLeaseAsync` generic mock is registered in the `QueuePollerTests` constructor so every test in the class gets a working callback stub — including `RunAsync_WhenDelayCancelled_ShouldExitLoop` which has no per-test mock setup.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Wrong Func signature in QueuePollerTests ExecuteInLeaseAsync mocks**

- **Found during:** Task 2 verification (test run)
- **Issue:** Wave 2 wrote per-test mocks using `Func<CancellationToken, Task>` (non-generic) for the two happy-path tests, but `QueuePoller.RunAsync` calls the generic `ExecuteInLeaseAsync<List<AtomizerJob>>` overload (`Func<CancellationToken, Task<List<AtomizerJob>>>`). NSubstitute didn't match, returned `null`, causing `NullReferenceException` at `leasedJobs.Count` (QueuePoller.cs line 102). Additionally, `RunAsync_WhenDelayCancelled_ShouldExitLoop` had no mock at all — the storage check fires on the first loop iteration because `_clock.MinValue` initialises `_lastStorageCheck` to `DateTimeOffset.MinValue`.
- **Fix:** Moved the `ExecuteInLeaseAsync` mock (with correct `Task<List<AtomizerJob>>` return type) to the constructor. Removed the duplicate per-test mocks from the two happy-path tests.
- **Files modified:** `tests/Atomizer.Tests/Processing/QueuePollerTests.cs`
- **Commit:** 4e636b1

## Test Results

- net8.0: Passed 74, Failed 0, Skipped 0
- net10.0: Passed 74, Failed 0, Skipped 0
- net6.0: Runtime not installed on this machine (expected — not a failure)

## Known Stubs

None. All test files fully exercise real mock patterns; no placeholder data flows to assertions.

## Threat Flags

None. Changes are test-only; no new network endpoints, auth paths, file access patterns, or schema changes introduced.

## Self-Check: PASSED

- `tests/Atomizer.Tests/Core/NoopLeasingScopeFactoryTests.cs` — deleted (FOUND)
- `tests/Atomizer.Tests/Processing/QueuePollerTests.cs` — exists and compiles (FOUND)
- `tests/Atomizer.Tests/Scheduling/SchedulePollerTests.cs` — exists and compiles (FOUND)
- Commit 036aa8a — exists (FOUND)
- Commit 4e636b1 — exists (FOUND)
- grep leasing refs in QueuePollerTests — 0 matches (PASS)
- grep leasing refs in SchedulePollerTests — 0 matches (PASS)
- dotnet test exits 0 — 74/74 passing (PASS)
