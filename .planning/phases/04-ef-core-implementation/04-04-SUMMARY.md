---
phase: 04-ef-core-implementation
plan: "04"
subsystem: test-infrastructure
tags: [ef-core, test-infra, migrations, schema-creation]
dependency_graph:
  requires: [04-01]
  provides: [clean-test-schema-creation]
  affects: [all-ef-core-integration-tests]
tech_stack:
  added: []
  patterns: [EnsureCreatedAsync-instead-of-MigrateAsync]
key_files:
  modified:
    - tests/Atomizer.EntityFrameworkCore.Tests/Fixtures/BaseDatabaseFixture.cs
  deleted:
    - tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/MySql/Migrations/ (3 files)
    - tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/Postgres/Migrations/ (3 files)
    - tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/Sqlite/Migrations/ (3 files)
    - tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/SqlServer/Migrations/ (3 files)
    - tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/MySql/MySqlDesignTimeDbContextFactory.cs
    - tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/Postgres/PostgresDesignTimeDbContextFactory.cs
    - tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/Sqlite/SqliteDesignTimeDbContextFactory.cs
    - tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/SqlServer/SqlServerDesignTimeDbContextFactory.cs
decisions:
  - "EnsureCreatedAsync replaces MigrateAsync in BaseDatabaseFixture — Testcontainers always starts a fresh DB so no migration history exists; EF model is the single source of truth for schema"
  - "All four Migrations/ folders deleted — removes coupling between test schema and migration files; schema always reflects current EF model including new unique index on JobKey"
metrics:
  duration: "< 5 minutes"
  completed: "2026-05-03"
  tasks_completed: 2
  files_modified: 1
  files_deleted: 16
---

# Phase 4 Plan 04: Test Infrastructure — EnsureCreatedAsync + Migration Cleanup Summary

## One-liner

Replaced `MigrateAsync` with `EnsureCreatedAsync` in the test fixture base class and deleted all 16 migration and design-time factory files, removing schema-migration coupling from the EF Core test project.

## What Was Done

### Task 1: Replace MigrateAsync with EnsureCreatedAsync

**File:** `tests/Atomizer.EntityFrameworkCore.Tests/Fixtures/BaseDatabaseFixture.cs`

Single-line change in `InitializeAsync`:

```csharp
// Before
await DbContext.Database.MigrateAsync();

// After
await DbContext.Database.EnsureCreatedAsync();
```

Rationale: Testcontainers always spins up a fresh database container per test run — no migration history exists to apply. `EnsureCreatedAsync` creates the schema directly from the current EF model, which now includes the unique index on `JobKey` added by Plan 01 (`HasIndex(e => e.JobKey).IsUnique()`). Using `MigrateAsync` would require generating a new migration file every time the EF model changes; `EnsureCreatedAsync` removes this coupling entirely.

### Task 2: Delete Migrations/ Directories and DesignTimeDbContextFactory Files

Deleted via `git rm -r`:

**Migrations/ directories (12 files total — 3 per provider):**
- `TestSetup/MySql/Migrations/` — `20250827155949_Initial.cs`, `.Designer.cs`, `MySqlDbContextModelSnapshot.cs`
- `TestSetup/Postgres/Migrations/` — `20250827155944_Initial.cs`, `.Designer.cs`, `PostgresDbContextModelSnapshot.cs`
- `TestSetup/Sqlite/Migrations/` — `20250828104901_Initial.cs`, `.Designer.cs`, `SqliteDbContextModelSnapshot.cs`
- `TestSetup/SqlServer/Migrations/` — `20250827155953_Initial.cs`, `.Designer.cs`, `SqlServerDbContextModelSnapshot.cs`

**DesignTimeDbContextFactory files (4 files):**
- `MySqlDesignTimeDbContextFactory.cs`
- `PostgresDesignTimeDbContextFactory.cs`
- `SqliteDesignTimeDbContextFactory.cs`
- `SqlServerDesignTimeDbContextFactory.cs`

**Retained (5 runtime DbContext files):**
- `MySqlDbContext.cs`, `PostgresDbContext.cs`, `SqliteDbContext.cs`, `SqlServerDbContext.cs`, `TestDbContext.cs`

## Verification Results

| Check | Result |
|-------|--------|
| `BaseDatabaseFixture.cs` contains `EnsureCreatedAsync` | PASS |
| `BaseDatabaseFixture.cs` does not contain `MigrateAsync` | PASS |
| Zero `Migrations/` directories under `TestSetup/` | PASS |
| Zero `*DesignTimeDbContextFactory.cs` files under `TestSetup/` | PASS |
| Exactly 5 `*DbContext.cs` runtime files retained | PASS |
| `dotnet build` exits 0 (42 warnings, 0 errors) | PASS |

## Deviations from Plan

None — plan executed exactly as written.

## Threat Surface Scan

No new network endpoints, auth paths, file access patterns, or schema changes at trust boundaries introduced. These are test-only infrastructure changes.

## Commits

| Task | Commit | Description |
|------|--------|-------------|
| Task 1 | `ef37f70` | refactor(04-04): replace MigrateAsync with EnsureCreatedAsync in BaseDatabaseFixture |
| Task 2 | `8371dd2` | chore(04-04): delete migrations and DesignTimeDbContextFactory from test project |

## Self-Check: PASSED

- `tests/Atomizer.EntityFrameworkCore.Tests/Fixtures/BaseDatabaseFixture.cs` — FOUND, contains `EnsureCreatedAsync`
- Commit `ef37f70` — FOUND
- Commit `8371dd2` — FOUND
- Zero Migrations/ directories — VERIFIED
- Zero DesignTimeDbContextFactory files — VERIFIED
- 5 DbContext files retained — VERIFIED
- Build passes (0 errors) — VERIFIED
