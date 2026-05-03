---
phase: 03-sql-dialect-strategy
plan: "03"
subsystem: EF Core Dialect Tests
tags: [test, sql-dialect, unit-test, no-containers]
dependency_graph:
  requires:
    - 03-01 (ISqlDialect interface and *Dialect classes)
  provides:
    - PostgreSqlDialectTests — 4 SQL keyword assertions for PostgreSQL dialect
    - SqlServerDialectTests — 4 SQL keyword assertions for SQL Server dialect
    - MySqlDialectTests — 4 SQL keyword assertions for MySQL dialect
  affects:
    - tests/Atomizer.EntityFrameworkCore.Tests/Providers/
tech_stack:
  added: []
  patterns:
    - In-memory EF Core model via ModelBuilder + FinalizeModel() (no DbContext, no containers)
    - EntityMap.Build() called directly in test BuildMaps() helper
key_files:
  created:
    - tests/Atomizer.EntityFrameworkCore.Tests/Providers/PostgreSqlDialectTests.cs
    - tests/Atomizer.EntityFrameworkCore.Tests/Providers/SqlServerDialectTests.cs
    - tests/Atomizer.EntityFrameworkCore.Tests/Providers/MySqlDialectTests.cs
  modified: []
decisions:
  - Cast UpsertScheduleAsync lambda to Action (not var) so AwesomeAssertions Throw<T> resolves correctly — Func<FormattableString> is not an Action
  - Added explicit `using AwesomeAssertions;` per project convention (not a global using in EF Core test project)
metrics:
  duration: "5 minutes"
  completed_date: "2026-05-03"
  tasks_completed: 1
  files_changed: 3
---

# Phase 3 Plan 03: SQL Dialect Tests Summary

Unit tests for all three SQL dialect classes using in-memory EF Core model construction (ModelBuilder + FinalizeModel); 12 tests (4 per dialect) pass on net8.0 and net10.0 with no Testcontainers dependency.

## What Was Built

Three test classes in `tests/Atomizer.EntityFrameworkCore.Tests/Providers/` that:

1. Build `EntityMap` instances from a minimal in-memory EF Core model (`ModelBuilder.AddAtomizerEntities` + `FinalizeModel()`) — no database, no containers.
2. Instantiate each dialect directly (`new PostgreSqlDialect(jobs, schedules)` etc.).
3. Assert that `GetDueJobs` format strings contain provider-distinguishing lock/limit keywords.
4. Assert that `GetDueSchedules` format strings contain the schedule-specific lock keyword.
5. Assert `ReleaseLeasedJobs` format strings contain `UPDATE`.
6. Assert `UpsertScheduleAsync` throws `NotImplementedException` (the Phase 4 stub).

### Keywords verified per provider

| Dialect | GetDueJobs keywords | GetDueSchedules keyword |
|---------|--------------------|-----------------------|
| PostgreSQL | `FOR NO KEY UPDATE SKIP LOCKED`, `LIMIT` | `FOR NO KEY UPDATE SKIP LOCKED` |
| SQL Server | `WITH (UPDLOCK, READPAST, ROWLOCK)`, `TOP(` | `WITH (UPDLOCK, READPAST, ROWLOCK)` |
| MySQL | `FOR UPDATE SKIP LOCKED`, `LIMIT` | `FOR UPDATE SKIP LOCKED` |

## Tasks Completed

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | Write PostgreSqlDialectTests, SqlServerDialectTests, MySqlDialectTests | a20eff6 | PostgreSqlDialectTests.cs, SqlServerDialectTests.cs, MySqlDialectTests.cs (created) |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Missing `using AwesomeAssertions;` directive**
- **Found during:** Task 1 build attempt
- **Issue:** The EF Core test project does not have a global `using AwesomeAssertions;` (unlike `Atomizer.Tests` which has it in `globals.cs`). The plan template omitted this using.
- **Fix:** Added `using AwesomeAssertions;` to all three test files.
- **Files modified:** All three dialect test files
- **Commit:** a20eff6

**2. [Rule 1 - Bug] `Func<FormattableString>` lambda not recognized by AwesomeAssertions `.Should().Throw<>()`**
- **Found during:** Task 1 build attempt
- **Issue:** `var act = () => dialect.UpsertScheduleAsync(null!)` infers `Func<FormattableString>` because the method returns `FormattableString` (even though it always throws). AwesomeAssertions' `Throw<T>` extension requires `Action`, not `Func<T>`.
- **Fix:** Changed `var act` to `Action act` in all three test files.
- **Files modified:** All three dialect test files
- **Commit:** a20eff6

## Known Stubs

None — these are test files only.

## Threat Flags

No new threat surface. Test-only files; never shipped in the NuGet package.

## Self-Check: PASSED

- FOUND: tests/Atomizer.EntityFrameworkCore.Tests/Providers/PostgreSqlDialectTests.cs
- FOUND: tests/Atomizer.EntityFrameworkCore.Tests/Providers/SqlServerDialectTests.cs
- FOUND: tests/Atomizer.EntityFrameworkCore.Tests/Providers/MySqlDialectTests.cs
- FOUND commit a20eff6 (Task 1)
- 12 tests pass: `dotnet test --filter FullyQualifiedName~Dialect` — Passed: 12 on net8.0 and net10.0
