---
phase: 08-inmemory-backend-unit-tests
plan: "03"
subsystem: tests
tags: [fifo, contract-tests, terminal-state, unblocking, fifo-13]
dependency_graph:
  requires: [08-02-PLAN.md]
  provides: [FIFO-13 terminal-state unblocking coverage]
  affects: [AtomizerStorageContractTests, InMemoryStorageContractTests]
tech_stack:
  added: []
  patterns: [xUnit Fact, domain-method-chain (Lease→Attempt→MarkAsCompleted/MarkAsFailed)]
key_files:
  created: []
  modified:
    - tests/Atomizer.Tests.Utilities/StorageContract/AtomizerStorageContractTests.cs
decisions:
  - "Used domain method chain (Lease→Attempt→MarkAsCompleted/MarkAsFailed) in test body to transition job1 to terminal state, consistent with existing test patterns"
metrics:
  duration: "~4 minutes"
  completed: "2026-05-04"
  tasks_completed: 1
  tasks_total: 1
  files_changed: 1
---

# Phase 08 Plan 03: Terminal-State Unblocking Tests Summary

Two contract tests added to AtomizerStorageContractTests covering FIFO-13 terminal-state unblocking: Completed and Failed head jobs release their partition for the next job.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Add terminal-unblocking tests to AtomizerStorageContractTests | ba8b01c | tests/Atomizer.Tests.Utilities/StorageContract/AtomizerStorageContractTests.cs |

## What Was Built

Added two `[Fact]` methods to `AtomizerStorageContractTests` under a `// FIFO-13: terminal-state unblocking` comment block, inserted immediately before the `// Helper` region:

1. **`GetDueJobsAsync_WhenPartitionHeadCompleted_ShouldUnblockNextJob`** — inserts two partitioned jobs, transitions job1 to `Completed` via `Lease → Attempt → MarkAsCompleted`, calls `UpdateJobsAsync`, then asserts `GetDueJobsAsync` returns job2.

2. **`GetDueJobsAsync_WhenPartitionHeadFailed_ShouldUnblockNextJob`** — same structure but transitions job1 to `Failed` via `Lease → Attempt → MarkAsFailed`.

Both tests are inherited by `InMemoryStorageContractTests` (and all future backend contract subclasses) without any additional code changes.

## Verification Results

- `InMemoryStorageContractTests` now runs **11 tests** (was 9 before this plan).
- **net8.0**: 11/11 passed.
- **net10.0**: 11/11 passed.
- net6.0 test host failed to launch (pre-existing environment issue — .NET 6 runtime not installed on this machine; 0 build errors).
- Both new tests confirmed passing in both supported runtimes.

Acceptance criteria:

```
grep -c "GetDueJobsAsync_WhenPartitionHeadCompleted_ShouldUnblockNextJob\|GetDueJobsAsync_WhenPartitionHeadFailed_ShouldUnblockNextJob" → 2
grep -c "MarkAsCompleted\|MarkAsFailed" → 4
```

Both output as expected.

## Deviations from Plan

None — plan executed exactly as written.

## Known Stubs

None.

## Threat Flags

None — test utilities file only; no new production code, network endpoints, auth paths, or schema changes introduced.

## Self-Check: PASSED

- [x] `tests/Atomizer.Tests.Utilities/StorageContract/AtomizerStorageContractTests.cs` modified — verified
- [x] Commit `ba8b01c` exists — verified
- [x] Both new test methods present (grep count = 2) — verified
- [x] All 11 InMemoryStorageContractTests pass on net8.0 and net10.0 — verified
