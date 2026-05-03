---
phase: 04-ef-core-implementation
plan: "03"
subsystem: ef-core-storage
tags: [ef-core, upsert, dialect, schedules, race-condition-fix]
dependency_graph:
  requires:
    - 04-01 (EntityFrameworkCoreStorage.ExecuteInLeaseAsync, LockTimeout)
    - 04-02 (dialect UpsertScheduleAsync SQL implementations)
  provides:
    - EntityFrameworkCoreStorage.UpsertScheduleAsync dialect routing
  affects:
    - src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs
tech_stack:
  added: []
  patterns:
    - "Provider-guard pattern: supported provider → dialect SQL via ExecuteSqlInterpolatedAsync"
    - "Unsafe fallback path with explicit race-safe comment for SQLite/AllowUnsafeProviderFallback"
    - "NotSupportedException for unsupported providers without fallback flag"
key_files:
  created: []
  modified:
    - src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs
decisions:
  - "UpsertScheduleAsync uses ExecuteSqlInterpolatedAsync (not FromSqlInterpolated) — upsert is non-SELECT DML, same as ReleaseLeasedAsync"
  - "Old check-then-insert retained only in AllowUnsafeProviderFallback path with explicit Not race-safe comment"
  - "Log message de-duplicated: removed duplicate schedule.JobKey arg that was passed as both ScheduleKey and JobKey placeholders"
metrics:
  duration: "60s"
  completed_date: "2026-05-03"
  tasks_completed: 1
  tasks_total: 1
  files_modified: 1
---

# Phase 4 Plan 03: UpsertScheduleAsync Dialect Routing Summary

## One-liner

Wire `EntityFrameworkCoreStorage.UpsertScheduleAsync` to dialect-native SQL for supported providers, eliminating the check-then-insert race condition for PostgreSQL, SQL Server, and MySQL.

## What Was Built

Single targeted change to `EntityFrameworkCoreStorage.UpsertScheduleAsync`:

The old check-then-insert method (with `@todo` race-condition comment) was replaced with the three-branch provider-guard pattern established elsewhere in the class:

1. **Supported provider + dialect available** — calls `_providerCache.Dialect.UpsertScheduleAsync(schedule)` (returns `FormattableString` synchronously), then executes via `_dbContext.Database.ExecuteSqlInterpolatedAsync`. Returns `entity.Id`. This is the production path for PostgreSQL, SQL Server, and MySQL.

2. **Unsupported provider + `AllowUnsafeProviderFallback`** — retains the original EF check-then-insert with the required comment `// Not race-safe — only used for test-only providers (SQLite) via AllowUnsafeProviderFallback`. Also fixes the pre-existing log message bug: the old code passed `schedule.JobKey` as both `{ScheduleKey}` and `{JobKey}` args — the new message uses a single `{JobKey}` placeholder with one arg.

3. **Unsupported provider without fallback flag** — throws `NotSupportedException` with the standard message used throughout the class.

## Commits

| Task | Commit | Description |
|------|--------|-------------|
| Task 1 | `7df0608` | feat(04-03): route UpsertScheduleAsync to dialect for supported providers |

## Deviations from Plan

None — plan executed exactly as written.

Minor log message fix (Rule 1 — Bug): The original `LogError` call passed `schedule.JobKey` as both arguments to `"Failed to upsert schedule {ScheduleKey} for job {JobKey}"`. This was a pre-existing bug where both format placeholders received the same value. The new fallback path uses `"Failed to upsert schedule for job {JobKey}"` with a single argument — no behavioral change, but removes the misleading duplicate placeholder.

## Threat Flags

None — no new network endpoints, auth paths, file access patterns, or schema changes at trust boundaries. T-04-07 (race condition elimination for supported providers) is now mitigated. T-04-08 (SQLite fallback race) is accepted and documented with the required comment.

## Self-Check: PASSED

- [x] `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` — FOUND
- [x] `grep "Dialect.UpsertScheduleAsync"` — FOUND (line 161)
- [x] `grep "ExecuteSqlInterpolatedAsync"` in UpsertScheduleAsync — FOUND (line 162)
- [x] `grep "Not race-safe"` — exactly 1 match (line 168)
- [x] `grep "race condition\|optimistic concurrency\|Look into"` — 0 matches
- [x] `grep "AllowUnsafeProviderFallback"` — present in fallback branch (line 166)
- [x] `dotnet build` exits 0 (0 errors, 8 pre-existing NU1903 warnings)
- [x] Commit `7df0608` present in git log
