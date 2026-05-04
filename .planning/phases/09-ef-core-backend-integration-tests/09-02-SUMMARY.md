---
phase: 09-ef-core-backend-integration-tests
plan: "02"
subsystem: ef-core-storage
tags:
  - fifo
  - sql-dialects
  - partitioned-insert
  - cte
  - sequence-number
dependency_graph:
  requires:
    - "09-01"
  provides:
    - "FIFO-aware GetDueJobs SQL (all three providers)"
    - "InsertJobWithSequence SQL (all three providers)"
    - "Partitioned insert branch in EntityFrameworkCoreStorage"
    - "CR-01 idempotency SequenceNumber fix"
  affects:
    - "09-03"
tech_stack:
  added: []
  patterns:
    - "CTE blocked_partitions + partition_heads for FIFO-aware job acquisition"
    - "COALESCE(MAX(SequenceNumber), 0) + 1 atomic sequence assignment per (queue, partition_key)"
    - "Derived-table subquery form for MySQL and PostgreSQL INSERT...SELECT on same table"
    - "Provider-specific locking: FOR NO KEY UPDATE SKIP LOCKED (PG), WITH (UPDLOCK,READPAST,ROWLOCK) on outer FROM (MSSQL), FOR UPDATE SKIP LOCKED (MySQL)"
key_files:
  created: []
  modified:
    - src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlDialect.cs
    - src/Atomizer.EntityFrameworkCore/Providers/Sql/SqlServerDialect.cs
    - src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlDialect.cs
    - src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs
decisions:
  - "MySQL InsertJobWithSequence uses derived-table COALESCE form to avoid 'can't specify target table for update in FROM clause' restriction"
  - "PostgreSQL InsertJobWithSequence also uses derived-table form for defensive consistency (same table subquery edge case)"
  - "SQL Server TOP(batchSize) is C# string-interpolated into the format string (integer literal, not an EF parameter)"
  - "SQL Server WITH (UPDLOCK, READPAST, ROWLOCK) applied only to outer FROM clause; CTE bodies have no hints to prevent lock escalation"
metrics:
  duration: "~15 minutes"
  completed: "2026-05-04"
  tasks_completed: 2
  tasks_total: 2
  files_modified: 4
  commits: 2
---

# Phase 09 Plan 02: FIFO SQL Implementation — Dialect Methods and Storage Wiring Summary

All three SQL dialect classes now implement `InsertJobWithSequence` with atomic per-(queue, partition_key) sequence assignment, and FIFO-aware `GetDueJobs` using `blocked_partitions` + `partition_heads` CTEs. `EntityFrameworkCoreStorage.InsertAsync` routes partitioned jobs through the dialect SQL path and reads back the assigned `SequenceNumber`, with a CR-01 fix assigning the existing `SequenceNumber` on idempotency collision.

## Tasks Completed

| Task | Description | Commit |
|------|-------------|--------|
| 1 | InsertJobWithSequence + CTE GetDueJobs in all three dialect classes | bd51d1b |
| 2 | EntityFrameworkCoreStorage.InsertAsync partitioned branch + CR-01 fix | c027831 |

## What Was Built

### Task 1 — Dialect SQL Methods (all three providers)

**`GetDueJobs` replaced with CTE approach:**

Every dialect now emits a two-CTE query:
1. `blocked_partitions` — collects partition keys that are currently `Processing` OR have been previously attempted (`Pending` with `Attempts > 0`); these are invisible to the poller.
2. `partition_heads` — finds the lowest `SequenceNumber` per unblocked partition.

The outer `SELECT` then returns:
- Unpartitioned jobs (`PartitionKey IS NULL`) matching the existing eligibility conditions.
- Partitioned jobs where the job is the `partition_head` (head-of-partition only).

Provider-specific locking is placed exclusively on the outer `FROM` clause:
- PostgreSQL: `FOR NO KEY UPDATE SKIP LOCKED` at end of outer SELECT
- SQL Server: `WITH (UPDLOCK, READPAST, ROWLOCK)` on `FROM {table} AS t` in outer SELECT only; CTE bodies have no hints
- MySQL: `FOR UPDATE SKIP LOCKED` at end of outer SELECT; `partition_heads` CTE uses LEFT JOIN anti-join pattern instead of `NOT IN (subquery on same table)`

**`InsertJobWithSequence` added to all three dialects:**

Uses `INSERT INTO ... SELECT ... COALESCE(MAX(SequenceNumber), 0) + 1` pattern, scoped to `(queue_key, partition_key)`. All 17 fields are passed as EF parameterized placeholders — no string concatenation of user values.

Both MySQL and PostgreSQL use the derived-table form for the `COALESCE(MAX(...))` subquery (`SELECT MAX(...) FROM (SELECT ... FROM {table} WHERE ...) AS sub`) to avoid the "can't specify target table for update in FROM clause" restriction that affects MySQL 5.x and some edge cases in PostgreSQL.

SQL Server uses the direct subquery form (no derived table needed).

### Task 2 — EntityFrameworkCoreStorage.InsertAsync

Two targeted changes:

**CR-01 fix (idempotency collision):** When an existing job is found with matching `IdempotencyKey`, `job.SequenceNumber = existing.SequenceNumber` is now assigned before returning `existing.Id`. This ensures the caller receives the originally assigned sequence number on a duplicate enqueue.

**Partitioned insert branch:** After the idempotency check, a new branch checks `job.PartitionKey != null && _providerCache is { IsSupportedProvider: true, Dialect: not null }`. If true, it:
1. Calls `_providerCache.Dialect.InsertJobWithSequence(job)` and executes it via `ExecuteSqlInterpolatedAsync`.
2. Reads back `SequenceNumber` from the database via a separate `FirstAsync` query on `JobEntities`.
3. Assigns `job.SequenceNumber = assigned` and returns `job.Id`.

The unpartitioned path (`JobEntities.Add(entity); await _dbContext.SaveChangesAsync(...)`) is unchanged.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Security/Correctness] Derived-table form for PostgreSQL InsertJobWithSequence**
- **Found during:** Task 1 implementation
- **Issue:** The plan noted MySQL requires derived-table COALESCE form to avoid same-table restriction; PostgreSQL is generally fine with direct subquery in INSERT...SELECT but the defensive form is safer and the plan explicitly allowed it
- **Fix:** Used `SELECT MAX(seq) FROM (SELECT SequenceNumber FROM {table} WHERE ...) AS sub` for PostgreSQL as well
- **Files modified:** src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlDialect.cs
- **Commit:** bd51d1b

## Known Stubs

None — all FIFO SQL implementation paths are fully wired.

## Threat Flags

None — all values passed to `FormattableStringFactory.Create` are EF-parameterized. Column/table names come from `EntityMap` (derived from EF model metadata, not user input). `TOP({batchSize})` in SQL Server is an integer literal interpolated at method call time, not a runtime user value.

## Self-Check: PASSED

Files exist:
- src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlDialect.cs — FOUND
- src/Atomizer.EntityFrameworkCore/Providers/Sql/SqlServerDialect.cs — FOUND
- src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlDialect.cs — FOUND
- src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs — FOUND

Commits exist:
- bd51d1b — FOUND
- c027831 — FOUND

Build: `dotnet build` — 0 errors

Acceptance criteria all verified:
- `InsertJobWithSequence` count = 1 in each dialect file
- `blocked_partitions` count >= 1 in each dialect file
- `WITH (UPDLOCK, READPAST, ROWLOCK)` on outer FROM only in SqlServerDialect
- `FOR UPDATE SKIP LOCKED` on outer SELECT only in MySqlDialect
- `FOR NO KEY UPDATE SKIP LOCKED` on outer SELECT only in PostgreSqlDialect
- `InsertJobWithSequence` count = 1 in EntityFrameworkCoreStorage.cs
- `job.SequenceNumber = existing.SequenceNumber` count = 1 (non-comment line)
- `job.SequenceNumber = assigned` count = 1
- `job.PartitionKey != null` count = 1
