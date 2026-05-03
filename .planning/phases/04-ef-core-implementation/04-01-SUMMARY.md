---
phase: 04-ef-core-implementation
plan: "01"
subsystem: ef-core-storage
tags: [ef-core, leasing, transaction, options, schema]
dependency_graph:
  requires: []
  provides:
    - EntityFrameworkCoreStorage.ExecuteInLeaseAsync (both overloads)
    - EntityFrameworkCoreJobStorageOptions.LockTimeout
    - AtomizerScheduleEntityConfiguration unique index on JobKey
  affects:
    - src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs
    - src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreJobStorageOptions.cs
    - src/Atomizer.EntityFrameworkCore/Configurations/AtomizerScheduleEntityConfiguration.cs
tech_stack:
  added: []
  patterns:
    - DatabaseTransactionLeasingScope.StartTransaction for ReadCommitted lease transactions
    - await using scope pattern for auto-commit/rollback on DisposeAsync
key_files:
  created: []
  modified:
    - src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreJobStorageOptions.cs
    - src/Atomizer.EntityFrameworkCore/Configurations/AtomizerScheduleEntityConfiguration.cs
    - src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs
decisions:
  - "LockTimeout property name (not LockAcquisitionTimeout) — shortest and unambiguous per Claude's discretion"
  - "AsNoTracking removed globally from GetDueJobsAsync SQL path — simpler since method does not cache entities"
  - "GetDueSchedulesAsync SQL path retains AsNoTracking — schedules are SELECT-only, not updated within the same transaction"
metrics:
  duration: "108s"
  completed_date: "2026-05-03"
  tasks_completed: 3
  tasks_total: 3
  files_modified: 3
---

# Phase 4 Plan 01: EF Core Foundational Changes Summary

## One-liner

ReadCommitted transaction leasing via `DatabaseTransactionLeasingScope.StartTransaction` with 30s default timeout, unique index on `JobKey` for race-safe upsert SQL.

## What Was Built

Three targeted changes that unblock all subsequent Phase 4 plans:

1. **`EntityFrameworkCoreJobStorageOptions.LockTimeout`** — New `TimeSpan` property (default 30s) consumed by both `ExecuteInLeaseAsync` overloads when calling `DatabaseTransactionLeasingScope.StartTransaction`. If acquisition times out, the poller silently skips the tick.

2. **Unique index on `JobKey`** in `AtomizerScheduleEntityConfiguration` — `builder.HasIndex(e => e.JobKey).IsUnique()` appended as the last statement in `Configure`. Required by the `ON CONFLICT (job_key)` / `MERGE ON job_key` SQL added in Plan 02/03.

3. **`ExecuteInLeaseAsync` both overloads implemented** in `EntityFrameworkCoreStorage` — Both previously threw `NotImplementedException`. Now each:
   - Calls `await DatabaseTransactionLeasingScope.StartTransaction(_dbContext, _options.LockTimeout, cancellationToken)`
   - Returns silently (`default!` / returns) if `scope.Acquired == false` (timeout or failure)
   - Executes the callback and lets `DisposeAsync` auto-commit or auto-rollback
   - `await using` ensures the transaction lifecycle is managed correctly

4. **Removed `AsNoTracking()` from `GetDueJobsAsync` SQL path** (D-04) — EF now tracks fetched rows so `UpdateJobsAsync` can commit status changes within the same open transaction. The LINQ fallback path and `GetDueSchedulesAsync` retain `AsNoTracking()` where appropriate.

## Commits

| Task | Commit | Description |
|------|--------|-------------|
| Task 1 | `4804724` | feat(04-01): add LockTimeout property to EntityFrameworkCoreJobStorageOptions |
| Task 2 | `f94d90b` | feat(04-01): add unique index on JobKey in AtomizerScheduleEntityConfiguration |
| Task 3 | `fbf8c8b` | feat(04-01): implement ExecuteInLeaseAsync and remove AsNoTracking from SQL path |

## Deviations from Plan

None — plan executed exactly as written.

## Threat Flags

None — no new network endpoints, auth paths, file access patterns, or schema changes at trust boundaries beyond what the plan's threat model already covers (T-04-01, T-04-02, T-04-03 all addressed by the implementation).

## Self-Check: PASSED

- [x] `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreJobStorageOptions.cs` — contains `LockTimeout` with `TimeSpan.FromSeconds(30)`
- [x] `src/Atomizer.EntityFrameworkCore/Configurations/AtomizerScheduleEntityConfiguration.cs` — contains `builder.HasIndex(e => e.JobKey).IsUnique()`
- [x] `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` — no `NotImplementedException`, 2 `StartTransaction` calls, `AsNoTracking` removed from SQL path
- [x] `dotnet build` exits 0 (0 errors, 8 pre-existing NU1903 warnings)
- [x] Commits `4804724`, `f94d90b`, `fbf8c8b` present in git log
