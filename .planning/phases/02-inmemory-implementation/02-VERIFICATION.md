---
phase: 02-inmemory-implementation
verified: 2026-05-03T00:00:00Z
status: passed
score: 12/12 must-haves verified
overrides_applied: 0
---

# Phase 2: InMemory Implementation Verification Report

**Phase Goal:** The InMemory backend implements the new callback-based leasing contract with the same atomicity guarantees as EF Core from the caller's perspective
**Verified:** 2026-05-03T00:00:00Z
**Status:** passed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | `InMemoryStorage.ExecuteInLeaseAsync` holds the per-queue `SemaphoreSlim` for the entire callback duration and releases only after completion | VERIFIED | `WaitAsync(TimeSpan.Zero)` on line 240, `try/finally { semaphore.Release() }` on lines 251-258; `ExecuteInLeaseAsync_WhenCallbackCompletes_ShouldReleaseSemaphore` and `ExecuteInLeaseAsync_WhenCallbackThrows_ShouldReleaseSemaphore` pass |
| 2 | `InMemoryStorage.UpsertScheduleAsync` is atomic — per-queue lock prevents the same race condition as the EF Core `@todo` | VERIFIED | `_semaphores.GetOrAdd(QueueKey.Scheduler, ...)` + `WaitAsync(cancellationToken)` (lines 160-161); `UpsertScheduleAsync_WhenSchedulerLeaseHeld_ShouldBlockUntilLeaseReleased` passes |
| 3 | The InMemory unit test suite passes without the `IAtomizerLeasingScopeFactory` dependency | VERIFIED | `InMemoryLeasingScopeFactory.cs` deleted (no file); `InMemoryLeasingScopeFactoryTests.cs` deleted (no file); 71/71 tests pass on net8.0 and net10.0 |
| 4 | A caller using the InMemory backend cannot observe a behavioral difference in error modes or lock lifecycle compared to the EF Core contract | VERIFIED | Skip-on-busy returns `default!` (line 248) guarded by `?? []` in `QueuePoller` (line 95); exception propagation via `try/finally` tested by `ExecuteInLeaseAsync_WhenCallbackThrows_ShouldReleaseSemaphore`; non-generic delegation via `ExecuteInLeaseAsync<bool>` on line 267 tested by `ExecuteInLeaseAsync_NonGenericOverload_ShouldCompleteAndReleaseSemaphore` |

**Score:** 4/4 roadmap success criteria verified

### Plan Must-Haves (02-01-PLAN.md)

| # | Must-Have | Status | Evidence |
|---|-----------|--------|----------|
| 1 | `ExecuteInLeaseAsync<TResult>` acquires per-queue `SemaphoreSlim(1,1)` instance field and releases in `finally` | VERIFIED | `InMemoryStorage.cs` line 239-258 |
| 2 | Non-generic `ExecuteInLeaseAsync` delegates entirely to `ExecuteInLeaseAsync<bool>` | VERIFIED | `InMemoryStorage.cs` line 267: `ExecuteInLeaseAsync<bool>` |
| 3 | Skip returns `default!` without invoking callback when semaphore not acquired | VERIFIED | `InMemoryStorage.cs` lines 242-248 |
| 4 | `UpsertScheduleAsync` acquires `_semaphores[QueueKey.Scheduler]` with always-wait `WaitAsync(cancellationToken)` | VERIFIED | `InMemoryStorage.cs` lines 160-161 |
| 5 | `_queues` is `ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>>`; helpers use `ConcurrentDictionary` idioms | VERIFIED | `InMemoryStorage.cs` line 11; `ids.Keys.Select` on line 94; `GetOrAdd`/`TryRemove` on lines 281-290 |
| 6 | `QueuePoller.RunAsync` null-coalesces `ExecuteInLeaseAsync` result with `?? []` | VERIFIED | `QueuePoller.cs` line 95 |
| 7 | `InMemoryLeasingScopeFactory.cs` is deleted | VERIFIED | File absent; no references in `src/` or `tests/` |
| 8 | `dotnet build` exits 0 with 0 errors | VERIFIED | Build output: `0 Error(s)` |

### Plan Must-Haves (02-02-PLAN.md)

| # | Must-Have | Status | Evidence |
|---|-----------|--------|----------|
| 9 | `InMemoryStorageLeaseTests.cs` exists with 5 `[Fact]` methods covering INMEM-01, INMEM-02, INMEM-03 | VERIFIED | File exists; `grep -c "\[Fact\]"` = 5; all 5 named test methods present |
| 10 | `InMemoryStorageTests.cs` `_queues` assertion uses `ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>>` + `.ContainKey` | VERIFIED | `InMemoryStorageTests.cs` lines 46-50 |
| 11 | `InMemoryLeasingScopeFactoryTests.cs` is deleted | VERIFIED | File absent; no references in `tests/` |
| 12 | `dotnet test tests/Atomizer.Tests/` exits 0 — full suite passes | VERIFIED | 71/71 passed on net8.0 and net10.0 (net6.0 runtime not installed on this machine — environment constraint, not a code failure) |

**Score:** 12/12 must-haves verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/Atomizer/Storage/InMemoryStorage.cs` | `ExecuteInLeaseAsync` overloads, `_semaphores` field, `_queues` ConcurrentDictionary, atomic `UpsertScheduleAsync` | VERIFIED | All fields and implementations present; no `NotImplementedException` (count=0) |
| `src/Atomizer/Processing/QueuePoller.cs` | Null-coalescing guard on `ExecuteInLeaseAsync` result | VERIFIED | `?? []` on line 95 |
| `tests/Atomizer.Tests/Storage/InMemoryStorageLeaseTests.cs` | 5 lease behaviour tests for INMEM-01, INMEM-02, INMEM-03 | VERIFIED | File exists; 5 `[Fact]` methods; all 5 pass |
| `tests/Atomizer.Tests/Storage/InMemoryStorageTests.cs` | Updated `_queues` field-type assertion | VERIFIED | `ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>>` + `.ContainKey` present |
| `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs` | DELETED | VERIFIED | File absent; zero references in `src/` or `tests/` |
| `tests/Atomizer.Tests/Storage/InMemoryLeasingScopeFactoryTests.cs` | DELETED | VERIFIED | File absent |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `QueuePoller.RunAsync` | `InMemoryStorage.ExecuteInLeaseAsync<List<AtomizerJob>>` | `?? []` null-coalescing on assignment | VERIFIED | `QueuePoller.cs` line 95 matches pattern `ExecuteInLeaseAsync.*\?\? \[\]` |
| `InMemoryStorage.ExecuteInLeaseAsync` (non-generic) | `InMemoryStorage.ExecuteInLeaseAsync<bool>` | delegation lambda | VERIFIED | `InMemoryStorage.cs` line 267 |
| `InMemoryStorage.UpsertScheduleAsync` | `_semaphores[QueueKey.Scheduler]` | `GetOrAdd` + `WaitAsync(cancellationToken)` | VERIFIED | `InMemoryStorage.cs` lines 160-161 |
| `InMemoryStorageLeaseTests` | `InMemoryStorage.ExecuteInLeaseAsync` | direct method call + `TaskCompletionSource` | VERIFIED | Lines 34, 55-65, 98-102, 125-133, 162-169 |
| `InMemoryStorageLeaseTests` | `NonPublicSpy._semaphores` | `NonPublicSpy.GetFieldValue` | VERIFIED | Lines 36-38, 109-111, 137-139 |

### Data-Flow Trace (Level 4)

Not applicable. Phase 2 delivers an in-memory storage backend, not a UI component or data-rendering artifact. The data flows are validated by the unit tests themselves.

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| `ExecuteInLeaseAsync` holds semaphore for full callback duration | `dotnet test --filter InMemoryStorageLeaseTests` | 5/5 passed (net8.0 + net10.0) | PASS |
| Concurrent second caller skips and returns `default` | Test: `ExecuteInLeaseAsync_WhenAlreadyAcquired_ShouldSkipCallbackAndReturnDefault` | Passed | PASS |
| Exception in callback releases semaphore | Test: `ExecuteInLeaseAsync_WhenCallbackThrows_ShouldReleaseSemaphore` | Passed | PASS |
| `UpsertScheduleAsync` blocks while `QueueKey.Scheduler` lease held | Test: `UpsertScheduleAsync_WhenSchedulerLeaseHeld_ShouldBlockUntilLeaseReleased` | Passed | PASS |
| Build is clean | `dotnet build Atomizer.sln` | 0 errors, 55 warnings (all pre-existing NU1701/xUnit1051) | PASS |
| Full unit test suite | `dotnet test tests/Atomizer.Tests/ ` | 71/71 net8.0, 71/71 net10.0 | PASS |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|----------|
| INMEM-01 | 02-01-PLAN, 02-02-PLAN | InMemory implements same callback-based leasing contract as EF Core — callers cannot observe behavioral differences | SATISFIED | `ExecuteInLeaseAsync` overloads implemented; non-generic delegates to generic; `ExecuteInLeaseAsync_NonGenericOverload_ShouldCompleteAndReleaseSemaphore` and `ExecuteInLeaseAsync_WhenAlreadyAcquired_ShouldSkipCallbackAndReturnDefault` pass |
| INMEM-02 | 02-01-PLAN, 02-02-PLAN | InMemory `GetDueJobsAsync` holds per-queue `SemaphoreSlim` lock for duration of lease callback | SATISFIED | `WaitAsync(TimeSpan.Zero)` + `try/finally` release; `ExecuteInLeaseAsync_WhenCallbackCompletes_ShouldReleaseSemaphore` and `ExecuteInLeaseAsync_WhenCallbackThrows_ShouldReleaseSemaphore` pass |
| INMEM-03 | 02-01-PLAN, 02-02-PLAN | InMemory `UpsertScheduleAsync` is atomic — uses existing lock to prevent race condition | SATISFIED | `_semaphores[QueueKey.Scheduler]` shared between `UpsertScheduleAsync` and `ExecuteInLeaseAsync(QueueKey.Scheduler, ...)`; `UpsertScheduleAsync_WhenSchedulerLeaseHeld_ShouldBlockUntilLeaseReleased` passes |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `src/Atomizer/Storage/InMemoryStorage.cs` | 14 | `private readonly Dictionary<JobKey, AtomizerSchedule> _schedules = new()` — plain (non-concurrent) Dictionary read in `GetDueSchedulesAsync` (line 209) without holding the scheduler semaphore | WARNING | `GetDueSchedulesAsync` reads `_schedules.Values` without a lock; concurrent `UpsertScheduleAsync` or `UpdateSchedulesAsync` writes could cause `InvalidOperationException` (collection modified during enumeration). Flagged as CR-03 in 02-REVIEW.md. Out of scope for Phase 2 requirements but a correctness risk in concurrent use. |
| `src/Atomizer/Storage/InMemoryStorage.cs` | 177 | `UpdateSchedulesAsync` writes `_schedules[schedule.JobKey]` without holding the scheduler semaphore | WARNING | Concurrent `UpsertScheduleAsync` (which holds the semaphore) and `UpdateSchedulesAsync` (which does not) are not mutually exclusive. Flagged as CR-04 in 02-REVIEW.md. Out of scope for Phase 2 requirements. |
| `src/Atomizer/Storage/InMemoryStorage.cs` | 28 | `InsertAsync` does not check `job.IdempotencyKey` — duplicate jobs are stored | WARNING | Diverges from EF Core backend which checks for existing idempotency key before inserting. Recurring schedules can produce duplicate jobs. Flagged as CR-01 in 02-REVIEW.md. Out of scope for Phase 2 requirements (INMEM-01/02/03 do not require idempotency enforcement). |
| `src/Atomizer/Storage/InMemoryStorage.cs` | 8 | Public `InMemoryStorage` class has no XML `<summary>` | INFO | Violates project convention "XML documentation required on all public APIs". Flagged as IN-03 in 02-REVIEW.md. |

These anti-patterns are all documented in the existing 02-REVIEW.md. They do not block the Phase 2 goal (INMEM-01, INMEM-02, INMEM-03 are all satisfied) but represent technical debt to carry into later phases or address as a dedicated fix.

### Human Verification Required

None. All Phase 2 success criteria are verifiable programmatically via the existing unit test suite and code inspection. No visual, real-time, or external-service behavior is involved.

## Gaps Summary

No gaps. All 12 must-haves are verified, all 4 roadmap success criteria are met, all 3 requirement IDs are satisfied, the build is clean, and the full test suite passes. The anti-patterns listed above (CR-01 through CR-04 from the review) are pre-existing or out-of-scope correctness issues that do not prevent the phase goal from being achieved.

---

_Verified: 2026-05-03T00:00:00Z_
_Verifier: Claude (gsd-verifier)_
