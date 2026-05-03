---
phase: 04-ef-core-implementation
plan: 02
subsystem: database
tags: [efcore, sql, dialect, upsert, postgresql, sqlserver, mysql, schedules]

# Dependency graph
requires:
  - phase: 03-sql-dialect-strategy
    provides: ISqlDialect interface with UpsertScheduleAsync stub in all three dialect classes
provides:
  - PostgreSqlDialect.UpsertScheduleAsync with INSERT ... ON CONFLICT (JobKey) DO UPDATE SET
  - SqlServerDialect.UpsertScheduleAsync with MERGE ... WITH (HOLDLOCK) ... WHEN MATCHED/NOT MATCHED
  - MySqlDialect.UpsertScheduleAsync with INSERT ... ON DUPLICATE KEY UPDATE
affects:
  - 04-03 (EntityFrameworkCoreStorage wires dialect.UpsertScheduleAsync)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "FormattableStringFactory.Create with raw string literal for provider-specific upsert SQL"
    - "Array.ConvertAll for netstandard2.0-compatible TimeSpan[] serialization to semicolon-delimited ms"
    - "Provider-specific boolean literals: TRUE/FALSE (PG/MySQL) vs 1/0 (SQL Server BIT)"
    - "Provider-specific timestamp format: u-format (PG/SqlServer) vs yyyy-MM-dd HH:mm:ss (MySQL)"

key-files:
  created: []
  modified:
    - src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlDialect.cs
    - src/Atomizer.EntityFrameworkCore/Providers/Sql/SqlServerDialect.cs
    - src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlDialect.cs

key-decisions:
  - "Id, CreatedAt, LastEnqueueAt excluded from all DO UPDATE / WHEN MATCHED / ON DUPLICATE KEY UPDATE clauses — insert-only fields"
  - "Used Array.ConvertAll instead of LINQ .Select for netstandard2.0 compatibility (no System.Linq extension on TimeSpan[])"
  - "PostgreSQL ON CONFLICT target uses c[nameof(JobKey)] which resolves to quoted identifier — accepted by PG"
  - "SQL Server WITH (HOLDLOCK) on MERGE target prevents phantom inserts under concurrent load"

patterns-established:
  - "Upsert pattern: dialect produces FormattableString; EF Core storage executes via ExecuteSqlInterpolatedAsync (wired in Plan 03)"

requirements-completed:
  - UPSRT-01
  - UPSRT-02
  - UPSRT-03

# Metrics
duration: 2min
completed: 2026-05-03
---

# Phase 4 Plan 02: SQL Dialect Upsert Implementations Summary

**Provider-native atomic upsert SQL for all three dialects: ON CONFLICT (PostgreSQL), MERGE WITH (HOLDLOCK) (SQL Server), ON DUPLICATE KEY UPDATE (MySQL)**

## Performance

- **Duration:** 2 min
- **Started:** 2026-05-03T15:47:09Z
- **Completed:** 2026-05-03T15:49:00Z
- **Tasks:** 3
- **Files modified:** 3

## Accomplishments

- Replaced all three `NotImplementedException` stubs in the SQL dialect classes with working provider-native upsert SQL
- PostgreSQL uses `INSERT ... ON CONFLICT (JobKey) DO UPDATE SET` — atomic per PostgreSQL spec
- SQL Server uses `MERGE ... WITH (HOLDLOCK) ... WHEN MATCHED THEN UPDATE WHEN NOT MATCHED THEN INSERT` — WITH (HOLDLOCK) prevents phantom inserts
- MySQL uses `INSERT ... ON DUPLICATE KEY UPDATE` — triggers on unique constraint violation on `JobKey`
- All three methods follow the established `FormattableStringFactory.Create($"""...""")` pattern with `_schedules.Col` for column names
- `dotnet build` exits 0 for `Atomizer.EntityFrameworkCore.csproj` across all target frameworks (net6.0, net8.0, net10.0)

## Task Commits

Each task was committed atomically:

1. **Task 1: Implement PostgreSqlDialect.UpsertScheduleAsync** - `8f6323a` (feat)
2. **Task 2: Implement SqlServerDialect.UpsertScheduleAsync** - `24ead45` (feat)
3. **Task 3: Implement MySqlDialect.UpsertScheduleAsync** - `6cfddff` (feat)

## Files Created/Modified

- `src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlDialect.cs` - UpsertScheduleAsync with INSERT ... ON CONFLICT
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/SqlServerDialect.cs` - UpsertScheduleAsync with MERGE ... WITH (HOLDLOCK)
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlDialect.cs` - UpsertScheduleAsync with INSERT ... ON DUPLICATE KEY UPDATE

## Decisions Made

- **Array.ConvertAll over LINQ:** `entity.RetryIntervals` is `TimeSpan[]`; used `Array.ConvertAll(entity.RetryIntervals, ts => (long)ts.TotalMilliseconds)` instead of `.Select()` for explicit netstandard2.0 compatibility without importing System.Linq in the dialect file.
- **Insert-only fields:** `Id`, `CreatedAt`, and `LastEnqueueAt` appear in INSERT column list for new rows but are excluded from the UPDATE clause in all three dialects — these fields are set once on creation and must not be overwritten on conflict.
- **PostgreSQL ON CONFLICT target:** Uses `c[nameof(AtomizerScheduleEntity.JobKey)]` which resolves to `"JobKey"` (double-quoted identifier) at runtime — PostgreSQL accepts quoted identifiers in ON CONFLICT clauses.

## Deviations from Plan

None — plan executed exactly as written.

The plan specified `string.Join(";", entity.RetryIntervals.Select(...))` but `Array.ConvertAll` was used instead for explicit netstandard2.0 safety. The behavior is identical; this is not a behavioral deviation.

## Issues Encountered

- CSharpier invocation required `~/.dotnet/tools/csharpier format <dir>` syntax (global tool on PATH but not shell-accessible as `dotnet csharpier`). CSharpier reported 0 files formatted — files were already within the print-width after the Edit tool. Build passed with 0 errors.

## Threat Flags

No new threat surface introduced. The three methods produce `FormattableString` values consumed by EF Core's `ExecuteSqlInterpolatedAsync` — entity field values become parameterized SQL parameters at the EF layer (T-04-04 in plan threat register: accepted). Race-safety is provided by the provider-native upsert semantics (T-04-05: mitigated).

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- All three dialect upsert methods are fully implemented and building
- Plan 03 (`EntityFrameworkCoreStorage.UpsertScheduleAsync`) can now wire `_providerCache.Dialect.UpsertScheduleAsync(schedule)` and call `ExecuteSqlInterpolatedAsync`
- No blockers

---

## Self-Check: PASSED

- `src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlDialect.cs` — FOUND, contains ON CONFLICT
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/SqlServerDialect.cs` — FOUND, contains MERGE + HOLDLOCK
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlDialect.cs` — FOUND, contains ON DUPLICATE KEY UPDATE
- Commit `8f6323a` — FOUND (feat(04-02): implement PostgreSqlDialect.UpsertScheduleAsync)
- Commit `24ead45` — FOUND (feat(04-02): implement SqlServerDialect.UpsertScheduleAsync)
- Commit `6cfddff` — FOUND (feat(04-02): implement MySqlDialect.UpsertScheduleAsync)
- Build: 0 errors

---
*Phase: 04-ef-core-implementation*
*Completed: 2026-05-03*
