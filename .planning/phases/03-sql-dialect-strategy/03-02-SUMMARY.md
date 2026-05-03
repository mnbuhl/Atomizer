---
phase: 03-sql-dialect-strategy
plan: 02
subsystem: database
tags: [efcore, sql-dialect, strategy-pattern, entityframeworkcore]

requires:
  - phase: 03-01
    provides: ISqlDialect interface and *Dialect classes with Dialect property on RelationalProviderCache

provides:
  - EntityFrameworkCoreStorage wired to delegate all raw SQL through _providerCache.Dialect
  - Zero inline DatabaseProvider branching in storage class

affects: [04-upsert-schedule-dialect]

tech-stack:
  added: []
  patterns: ["Strategy delegation: storage calls ISqlDialect via _providerCache.Dialect, no provider switch"]

key-files:
  created: []
  modified:
    - src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs

key-decisions:
  - "All three call-site changes were applied as part of plan 03-01 auto-deviation (build would have failed otherwise)"

patterns-established:
  - "Null-guard pattern: _providerCache is { IsSupportedProvider: true, Dialect: not null } before delegating SQL"

requirements-completed: [DIAL-03, DIAL-04]

duration: 0min
completed: 2026-05-03
---

# Phase 03-02: Wire Dialect into EntityFrameworkCoreStorage Summary

**EntityFrameworkCoreStorage delegates all raw SQL through `_providerCache.Dialect` — no inline provider branching remains in the storage class**

## Performance

- **Duration:** 0 min (applied as auto-deviation in 03-01)
- **Completed:** 2026-05-03
- **Tasks:** 1/1
- **Files modified:** 1

## Accomplishments
- All three `RawSqlProvider` call sites in EntityFrameworkCoreStorage updated to `Dialect`
- Method names aligned: `GetDueJobs`, `ReleaseLeasedJobs`, `GetDueSchedules` (no Async suffix)
- Null-guard pattern updated: `{ IsSupportedProvider: true, Dialect: not null }` in all three locations
- Zero `DatabaseProvider` enum references remain in EntityFrameworkCoreStorage

## Task Commits

Applied as part of 03-01 deviation commit:

1. **Task 1: Update call sites** — included in `23c2257` (refactor(03-01): wire ISqlDialect into RelationalProviderCache and storage)

## Files Created/Modified
- `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` — three call sites updated to `_providerCache.Dialect.*`

## Decisions Made
None — the 03-01 agent applied this change as a required auto-deviation (the build would have failed without it).

## Deviations from Plan
None — this plan's work was incorporated into 03-01's execution as an auto-deviation to keep the build green. The outcome is identical to what this plan specified.

## Issues Encountered
None.

## Next Phase Readiness
- Storage class is fully dialect-agnostic — ready for Phase 4 to implement `UpsertScheduleAsync` in each dialect
- All three `_providerCache.Dialect` call sites confirmed present

---
*Phase: 03-sql-dialect-strategy*
*Completed: 2026-05-03*
