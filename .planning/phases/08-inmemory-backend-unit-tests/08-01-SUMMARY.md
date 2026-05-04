---
phase: 08-inmemory-backend-unit-tests
plan: "01"
subsystem: storage
tags: [fifo, inmemory, idempotency, partition-blocking, tdd]
dependency_graph:
  requires: []
  provides: [InMemoryStorage-FIFO, ScheduleProcessor-PartitionKey]
  affects: [08-02-PLAN.md]
tech_stack:
  added: []
  patterns:
    - ConcurrentDictionary nested per-queue partition sequence counter
    - Three-pass FIFO filter in GetDueJobsAsync (blockedPartitions + eligible + partitionHeads)
    - netstandard2.0-safe OrderBy().First() instead of MinBy()
key_files:
  created: []
  modified:
    - src/Atomizer/Storage/InMemoryStorage.cs
    - src/Atomizer/Scheduling/ScheduleProcessor.cs
    - tests/Atomizer.Tests/Storage/InMemoryStorageTests.cs
decisions:
  - CR-01 idempotency check placed before sequence assignment so no sequence number is consumed for a duplicate
  - Used ConcurrentDictionary<QueueKey, ConcurrentDictionary<string, long>> for per-(queue, partitionKey) sequences
  - Used job.PartitionKey.Key (string) as inner dict key to avoid value-object boxing
  - OrderBy(SequenceNumber).First() used instead of MinBy() for netstandard2.0 compatibility
metrics:
  duration: "4m 21s"
  completed: "2026-05-04"
  tasks_completed: 3
  files_modified: 3
---

# Phase 8 Plan 1: FIFO InMemoryStorage Implementation Summary

**One-liner:** FIFO sequence assignment and partition blocking in InMemoryStorage with full CR-01 idempotency fix, plus ScheduleProcessor partitionKey forwarding.

## What Was Built

Implemented FIFO ordering and partition blocking in `InMemoryStorage` (satisfying FIFO-10), and wired `ScheduleProcessor` to pass `schedule.PartitionKey` into `AtomizerJob.Create()` (D-07).

### Task 1: InsertAsync FIFO with idempotency fix (TDD)

Added `_partitionSequences` field (`ConcurrentDictionary<QueueKey, ConcurrentDictionary<string, long>>`) alongside `_queues`. Replaced the flat `InsertAsync` body with a three-step implementation:

1. **CR-01 idempotency check** — linear scan of `_jobs.Values` for a matching `IdempotencyKey`. On collision: assigns `existing.SequenceNumber` to the passed-in job and returns `existing.Id` without touching `_queues`, `_leasesByToken`, or `EvictCompletedAndFailed`.
2. **FIFO-09 sequence assignment** — `_partitionSequences.GetOrAdd` per queue, then `AddOrUpdate` for atomic increment per partition key. Sequence starts at 1. Unpartitioned jobs (`PartitionKey == null`) leave `SequenceNumber` null.
3. **Store + index** — unchanged from original.

### Task 2: GetDueJobsAsync three-pass FIFO filter (TDD)

Replaced the single-pass LINQ chain with three passes:

- **Pass 1:** Build `HashSet<string> blockedPartitions` by scanning all `ids.Keys` for jobs where `IsPartitionBlocked == true`. Uses the domain property rather than re-implementing the condition.
- **Pass 2:** Filter eligible candidates using the existing status/time logic, plus exclude jobs whose `PartitionKey.Key` is in `blockedPartitions`.
- **Pass 3:** Split eligible into `unpartitioned` and `partitionHeads`. Partition heads selected via `GroupBy(PartitionKey.Key).Select(g => g.OrderBy(SequenceNumber).First())` — netstandard2.0-safe alternative to `MinBy()`. Concat both, order by `ScheduledAt`/`CreatedAt`, take `batchSize`.

### Task 3: ScheduleProcessor PartitionKey forwarding

Added `partitionKey: schedule.PartitionKey` as the final named argument to `AtomizerJob.Create()` in `ScheduleProcessor.ProcessAsync`. One-line change.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] CSharpier formatting violation in InMemoryStorage.cs**
- **Found during:** Task 3 verification (CSharpier check)
- **Issue:** Two long lines in `InsertAsync` (GetOrAdd and AddOrUpdate calls) exceeded printWidth: 120
- **Fix:** Ran `csharpier format` on the file; wrapped GetOrAdd call across 3 lines
- **Files modified:** `src/Atomizer/Storage/InMemoryStorage.cs`
- **Commit:** 691d71a

## TDD Gate Compliance

| Task | RED commit | GREEN commit |
|------|-----------|-------------|
| Task 1 (InsertAsync) | 0846a12 — 5 failing tests | 1d7588a — all pass |
| Task 2 (GetDueJobsAsync) | 2abf01d — 2 failing tests | aa161ea — all pass |

Both RED and GREEN gates satisfied. RED commits preceded GREEN commits.

## Test Coverage

11 new unit tests added to `InMemoryStorageTests.cs`:

**InsertAsync FIFO tests (Task 1):**
- `InsertAsync_WhenPartitionedJob_ShouldAssignSequenceNumberStartingAtOne`
- `InsertAsync_WhenPartitionedJobsInDifferentQueues_ShouldAssignIndependentSequences`
- `InsertAsync_WhenUnpartitionedJob_ShouldLeaveSequenceNumberNull`
- `InsertAsync_WhenIdempotencyKeyCollision_ShouldReturnExistingIdAndAssignExistingSequenceNumber`
- `InsertAsync_WhenIdempotencyKeyCollision_ShouldNotIncreaseJobCount`

**GetDueJobsAsync FIFO tests (Task 2):**
- `GetDueJobsAsync_WhenTwoJobsSharePartition_ShouldReturnOnlyLowestSequenceNumber`
- `GetDueJobsAsync_WhenPartitionHeadAndUnpartitionedJobExist_ShouldReturnBoth`
- `GetDueJobsAsync_WhenPartitionJobIsProcessing_ShouldReturnEmpty`
- `GetDueJobsAsync_WhenPartitionJobIsPendingWithAttempts_ShouldReturnEmpty`
- `GetDueJobsAsync_WhenQueueABlockedPartitionSameKeyAsQueueB_ShouldReturnQueueBJobUnaffected`
- `GetDueJobsAsync_WhenProcessingJobHasExpiredVisibleAt_ShouldReturnIt`

## Verification Results

```
dotnet build — 0 errors (pre-existing NU1903 warnings only)
dotnet test tests/Atomizer.Tests — 104/104 passed (net8.0 + net10.0)
csharpier check — all files formatted
```

## Known Stubs

None — all data paths are fully wired.

## Threat Flags

None — all changes are internal in-process storage; no new network endpoints, public API surface, or trust boundary crossings introduced.

## Self-Check: PASSED

- `src/Atomizer/Storage/InMemoryStorage.cs` contains `_partitionSequences` field: confirmed
- `InsertAsync` contains `_jobs.Values.FirstOrDefault(j => j.IdempotencyKey == job.IdempotencyKey)`: confirmed
- `InsertAsync` contains `_partitionSequences.GetOrAdd` and `partitionSequences.AddOrUpdate`: confirmed
- `GetDueJobsAsync` contains `blockedPartitions` HashSet and `IsPartitionBlocked` usage: confirmed
- `GetDueJobsAsync` contains `partitionHeads` and `unpartitioned` variables: confirmed
- `ScheduleProcessor.cs` contains `partitionKey: schedule.PartitionKey`: confirmed
- Commits exist: 0846a12, 1d7588a, 2abf01d, aa161ea, 691d71a
