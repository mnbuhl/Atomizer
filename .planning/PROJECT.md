# Atomizer — Storage Refactor

## What This Is

Atomizer is a background job scheduling and queueing framework for ASP.NET Core, distributed as two NuGet packages (`Atomizer` core and `Atomizer.EntityFrameworkCore`). The v1.0 milestone delivered a clean, correct, and extensible storage abstraction — making it straightforward to add non-SQL backends (MongoDB, Redis) in future milestones.

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
- ✓ Callback-based leasing abstraction — `ExecuteInLeaseAsync(...)` replaces `IAtomizerLeasingScopeFactory/IAtomizerLeasingScope`; each backend implements its own atomicity strategy — v1.0
- ✓ InMemory backend fully aligned to the same callback-based leasing contract — same error modes and atomicity guarantees as EF Core from the caller's perspective — v1.0
- ✓ Provider SQL extracted into `ISqlDialect` strategy classes (one per provider: `PostgreSqlDialect`, `SqlServerDialect`, `MySqlDialect`) — v1.0
- ✓ `GetDueJobsAsync` and `GetDueSchedulesAsync` use `FOR UPDATE SKIP LOCKED` (or provider equivalent) inside the lease callback — v1.0
- ✓ Native upsert for `UpsertScheduleAsync` using provider-specific SQL — fixes the @todo race condition — v1.0
- ✓ IAtomizerStorage abstraction updated (breaking change) — v1.0
- ✓ XML documentation on all public APIs — zero CS1591 under TreatWarningsAsErrors=true — v1.0

### Active

*(Next milestone requirements to be defined via `/gsd-new-milestone`)*

- [ ] Address InMemoryStorage thread safety gaps: `_schedules` read without lock in `GetDueSchedulesAsync`/`UpdateSchedulesAsync` (CR-03, CR-04); `InsertAsync` missing idempotency key check (CR-01)
- [ ] EF Core: `queue` parameter ignored in `ExecuteInLeaseAsync` — no per-queue DB isolation (behavioral divergence from InMemory contract)
- [ ] MongoDB backend implementing `ExecuteInLeaseAsync` via distributed lock
- [ ] Redis backend implementing `ExecuteInLeaseAsync` via RedLock or Lua script

### Out of Scope

- SQLite as a production-supported provider — it lacks `FOR NO KEY UPDATE SKIP LOCKED`; stays as test-only via Testcontainers
- NuGet major version bump — project will not publish to NuGet during v1.0 milestone
- Changes to the public `IAtomizerClient` API (EnqueueAsync, ScheduleAsync, ScheduleRecurringAsync) — out of scope
- Changes to the processing pipeline (QueuePump, JobWorker, DefaultJobDispatcher) — out of scope
- Oracle provider — removed in #10, not being re-added

## Context

- **Shipped v1.0** on 2026-05-03: 5 phases, 17 plans, 227 commits, 253 files changed, ~6,020 LOC C#
- Current branch: `refactor/data`
- EF Core integration tests require Docker (Testcontainers) — runtime behavior not verified in static CI
- Known tech debt carried into next milestone: InMemoryStorage concurrency gaps (CR-01, CR-03, CR-04), EF Core per-queue isolation, UpsertScheduleAsync incorrect returned Id on conflict path

## Constraints

- **Compatibility**: `Atomizer` targets `netstandard2.0;net8.0;net10.0`. Use `#if NETCOREAPP3_0_OR_GREATER` for `IAsyncDisposable` / `await using`.
- **No Newtonsoft**: System.Text.Json only
- **Formatting**: CSharpier (`dotnet csharpier .`)
- **XML docs**: All public APIs must have XML documentation
- **No NuGet publish**: v1.0 milestone does not include package publication

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Callback-based leasing (`ExecuteInLeaseAsync`) | Transaction must span GetDueJobs + UpdateJobs; collapsing into storage prevents double-dispatch | ✓ Implemented — v1.0 Phase 1 |
| InMemory fully aligns to EF Core contract | Diverging behavior between backends makes integration tests misleading and surprises users switching backends | ✓ Implemented — v1.0 Phase 2 |
| Provider SQL extracted to `ISqlDialect` (internal) | Raw SQL currently scattered; dialect strategy makes adding new SQL providers safe and testable; `internal` prevents external implementations | ✓ Implemented — v1.0 Phase 3 |
| Native upsert per-provider (not EF Core ExecuteUpdate) | EF Core doesn't natively support upsert; provider-specific SQL is already the pattern used elsewhere | ✓ Implemented — v1.0 Phase 4 |
| COMPAT-02 dropped (no version bump) | Project will not publish to NuGet this milestone | ✓ Dropped — v1.0 Phase 5 |
| `queue` param unused in EF Core `ExecuteInLeaseAsync` | EF Core uses connection-scoped ReadCommitted transaction rather than per-queue locking — per-queue isolation is a future concern | ⚠️ Revisit — next milestone |

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
*Last updated: 2026-05-03 after v1.0 milestone*
