---
phase: 03-sql-dialect-strategy
plan: "01"
subsystem: EF Core Provider SQL
tags: [refactor, sql-dialect, strategy-pattern, efcore]
dependency_graph:
  requires: []
  provides:
    - ISqlDialect strategy interface with 4-method contract
    - PostgreSqlDialect, SqlServerDialect, MySqlDialect internal sealed implementations
    - RelationalProviderCache.Dialect property wired via CreateDialect()
  affects:
    - src/Atomizer.EntityFrameworkCore/Providers/
    - src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs
tech_stack:
  added: []
  patterns:
    - Strategy pattern (ISqlDialect replaces IDatabaseProviderSql)
key_files:
  created:
    - src/Atomizer.EntityFrameworkCore/Providers/ISqlDialect.cs
    - src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlDialect.cs
    - src/Atomizer.EntityFrameworkCore/Providers/Sql/SqlServerDialect.cs
    - src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlDialect.cs
  modified:
    - src/Atomizer.EntityFrameworkCore/Providers/RelationalProviderCache.cs
    - src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs
  deleted:
    - src/Atomizer.EntityFrameworkCore/Providers/IDatabaseProviderSql.cs
    - src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlProvider.cs
    - src/Atomizer.EntityFrameworkCore/Providers/Sql/SqlServerProvider.cs
    - src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlProvider.cs
decisions:
  - Method names drop misleading Async suffix (GetDueJobs, ReleaseLeasedJobs, GetDueSchedules); UpsertScheduleAsync retains suffix to align with the EF Core storage method it will be called from in Phase 4
  - ISqlDialect is internal (not public) per T-03-02 threat mitigation — consumers cannot implement or inspect it
  - UpsertScheduleAsync stub throws NotImplementedException annotated with TODO comment for Phase 4
metrics:
  duration: "2 minutes"
  completed_date: "2026-05-03"
  tasks_completed: 2
  files_changed: 10
---

# Phase 3 Plan 01: SQL Dialect Strategy — ISqlDialect Introduction Summary

Renamed `IDatabaseProviderSql` → `ISqlDialect` and `*Provider` classes → `*Dialect`, making all internal sealed; added `UpsertScheduleAsync` stub as the fourth interface method for Phase 4; wired `Dialect` property on `RelationalProviderCache` and updated all call sites.

## What Was Built

Established the Strategy pattern contract for provider-specific SQL generation. The `ISqlDialect` interface (4 methods) replaced the `public IDatabaseProviderSql` (3 methods), and the three provider classes were renamed to dialect classes, both gaining `internal sealed` visibility as required by the threat model. `RelationalProviderCache.Dialect` replaces `RawSqlProvider`, and `EntityFrameworkCoreStorage` was updated to use the new property and method names.

## Tasks Completed

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | Create ISqlDialect and three *Dialect classes, delete old *Provider files | db675ab | ISqlDialect.cs, PostgreSqlDialect.cs, SqlServerDialect.cs, MySqlDialect.cs (created); IDatabaseProviderSql.cs, PostgreSqlProvider.cs, SqlServerProvider.cs, MySqlProvider.cs (deleted) |
| 2 | Wire ISqlDialect into RelationalProviderCache | 23c2257 | RelationalProviderCache.cs, EntityFrameworkCoreStorage.cs |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] EntityFrameworkCoreStorage.cs referenced old RawSqlProvider and *Async method names**
- **Found during:** Task 2 verification (pre-build grep scan)
- **Issue:** `EntityFrameworkCoreStorage.cs` had 3 references to `RawSqlProvider` and called `GetDueJobsAsync`, `ReleaseLeasedJobsAsync`, `GetDueSchedulesAsync` — all removed by Task 1. The build would have failed without updating this file.
- **Fix:** Updated all 3 property access patterns to `Dialect` and renamed method calls to `GetDueJobs`, `ReleaseLeasedJobs`, `GetDueSchedules`.
- **Files modified:** `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs`
- **Commit:** 23c2257

## Known Stubs

| Stub | File | Reason |
|------|------|--------|
| `UpsertScheduleAsync` throws `NotImplementedException` | `PostgreSqlDialect.cs`, `SqlServerDialect.cs`, `MySqlDialect.cs` | Intentional per plan — Phase 4 will implement provider-specific native upsert SQL for each dialect |

## Threat Flags

No new threat surface introduced. `IDatabaseProviderSql` was `public`; `ISqlDialect` is `internal` — this reduces the attack surface (T-03-02 mitigated).

## Self-Check: PASSED

- FOUND: ISqlDialect.cs
- FOUND: PostgreSqlDialect.cs
- FOUND: SqlServerDialect.cs
- FOUND: MySqlDialect.cs
- DELETED: IDatabaseProviderSql.cs (OK)
- FOUND commit db675ab (Task 1)
- FOUND commit 23c2257 (Task 2)
