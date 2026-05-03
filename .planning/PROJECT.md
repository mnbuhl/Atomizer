# Atomizer — Storage Refactor

## What This Is

Atomizer is a background job scheduling and queueing framework for ASP.NET Core, distributed as two NuGet packages (`Atomizer` core and `Atomizer.EntityFrameworkCore`). This milestone refactors the entire storage layer to be cleaner, more correct, and extensible to non-SQL backends (MongoDB, Redis) in future milestones.

## Core Value

A storage abstraction so clean and correct that implementing a new backend requires no tribal knowledge — just the interface.

## Requirements

### Validated

- ✓ IAtomizerStorage contract for InsertAsync, UpdateJobsAsync, GetDueJobsAsync, ReleaseLeasedAsync, UpsertScheduleAsync, UpdateSchedulesAsync, GetDueSchedulesAsync — existing
- ✓ InMemory backend with SemaphoreSlim-based per-queue locking — existing
- ✓ EF Core backend with provider-specific raw SQL for atomic job acquisition — existing
- ✓ Retry strategies: Fixed, Exponential, Intervals — existing
- ✓ Recurring schedules via Cronos (6-part cron) with MisfirePolicy — existing
- ✓ AtomizerJob domain state transitions (Lease, Attempt, MarkAsCompleted, etc.) — existing
- ✓ Providers: PostgreSQL, SQL Server, MySQL — existing
- ✓ Idempotency key support on InsertAsync — existing

### Active

- [ ] Callback-based leasing abstraction — `ExecuteInLeaseAsync(...)` replaces `IAtomizerLeasingScopeFactory/IAtomizerLeasingScope`; each backend implements its own atomicity strategy
- [ ] `GetDueJobsAsync` and `GetDueSchedulesAsync` use `FOR UPDATE` (or provider equivalent) inside the lease callback to guarantee at-most-once dispatch
- [ ] Native upsert for `UpsertScheduleAsync` using provider-specific SQL (`ON CONFLICT` for PostgreSQL, `MERGE` for SQL Server, `INSERT ... ON DUPLICATE KEY UPDATE` for MySQL) — fixes the current @todo race condition
- [ ] Provider SQL extracted into `ISqlDialect` strategy classes (one per provider: `PostgreSqlDialect`, `SqlServerDialect`, `MySqlDialect`)
- [ ] InMemory backend fully aligned to the same callback-based leasing contract — same error modes and atomicity guarantees as EF Core from the caller's perspective
- [ ] IAtomizerStorage abstraction updated (breaking change) — shipped as a major version bump

### Out of Scope

- SQLite as a production-supported provider — it lacks `FOR NO KEY UPDATE SKIP LOCKED`; stays as test-only via Testcontainers
- MongoDB / Redis backend implementations — this milestone defines the abstraction that allows them; the implementations are future milestones
- Changes to the public `IAtomizerClient` API (EnqueueAsync, ScheduleAsync, ScheduleRecurringAsync) — out of scope
- Changes to the processing pipeline (QueuePump, JobWorker, DefaultJobDispatcher) — out of scope
- Entity schema changes (AtomizerJobEntity, AtomizerScheduleEntity table layout) — out of scope unless required by upsert approach

## Context

- Current branch: `refactor/data` — already set up for this work
- Recent overhauls landed: EF Core overhaul (#8), InMemory overhaul v2 (#9), .NET 10 support (#11), Oracle support removed (#10)
- Known `@todo` in `UpsertScheduleAsync`: non-atomic check-then-insert is a real race condition that this milestone must fix
- `IAtomizerLeasingScopeFactory` / `IAtomizerLeasingScope` is a public API — changing it is a breaking change requiring a major version bump
- The callback-based leasing approach must keep the transaction open between `GetDueJobsAsync` (acquires row locks) and `UpdateJobsAsync` (commits status) — this is a correctness invariant, not a nice-to-have
- Non-SQL providers (MongoDB, Redis) will use this same abstraction but implement their own atomicity strategy (distributed locks, optimistic retry, etc.) — the contract must not assume SQL

## Constraints

- **Compatibility**: netstandard2.1 target for core library — no C# 8+ features without `#if` guards
- **Breaking change**: IAtomizerStorage and leasing abstraction changes require a major version bump
- **No Newtonsoft**: System.Text.Json only
- **Formatting**: CSharpier (`dotnet csharpier .`)
- **XML docs**: All public APIs must have XML documentation

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Callback-based leasing (`ExecuteInLeaseAsync`) | Transaction must span GetDueJobs + UpdateJobs; collapsing into storage is not safe because committing before updating would allow double-dispatch | — Pending |
| Provider SQL extracted to `ISqlDialect` | Raw SQL currently scattered in one class; dialect strategy makes adding new SQL providers safe and testable | — Pending |
| Native upsert per-provider (not EF Core ExecuteUpdate) | EF Core doesn't natively support upsert; provider-specific SQL is already the pattern used elsewhere | — Pending |
| InMemory fully aligns to EF Core contract | Diverging behavior between backends makes integration tests misleading and surprises users switching backends | — Pending |
| Major version bump | IAtomizerStorage is public API; breaking the leasing interface is intentional and must be communicated explicitly | — Pending |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd-transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd-complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-05-03 after initialization*
