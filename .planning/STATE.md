# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-05-03)

**Core value:** A storage abstraction so clean and correct that implementing a new backend requires no tribal knowledge — just the interface.
**Current focus:** Phase 1 — Leasing Abstraction

## Current Position

Phase: 1 of 5 (Leasing Abstraction)
Plan: 0 of TBD in current phase
Status: Ready to plan
Last activity: 2026-05-03 — Roadmap created

Progress: [░░░░░░░░░░] 0%

## Performance Metrics

**Velocity:**
- Total plans completed: 0
- Average duration: -
- Total execution time: 0 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| - | - | - | - |

**Recent Trend:**
- Last 5 plans: —
- Trend: —

*Updated after each plan completion*

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- Callback-based leasing (`ExecuteInLeaseAsync`): Transaction must span GetDueJobs + UpdateJobs — collapsing into storage prevents double-dispatch
- `ISqlDialect` strategy: Provider SQL is scattered; dialect classes make adding SQL providers safe and testable
- Native upsert per-provider: EF Core has no native upsert; provider-specific SQL is already the established pattern
- InMemory aligns to EF Core contract: Diverging backends make integration tests misleading
- Major version bump: `IAtomizerStorage` is public API; breaking leasing interface must be communicated explicitly

### Pending Todos

None yet.

### Blockers/Concerns

- Phase 1 is the critical gate: nothing in phases 2-5 can proceed until `ExecuteInLeaseAsync` interface shape is finalized
- Phase 3 (ISqlDialect extraction) must complete before Phase 4 touches EF Core storage logic

## Deferred Items

Items acknowledged and carried forward from previous milestone close:

| Category | Item | Status | Deferred At |
|----------|------|--------|-------------|
| Tech debt | Idempotency key unique index (read-before-write race) | Deferred | Pre-milestone |
| Tech debt | EF UpdateRange writes all columns (no targeted UPDATE) | Deferred | Pre-milestone |
| Bug | Static `Semaphores` dictionary process-wide (test isolation) | Deferred | Pre-milestone |
| Bug | Stale acquiredTimestamp in InMemory lock tuple | Addressed by INMEM refactor |

## Session Continuity

Last session: 2026-05-03
Stopped at: Roadmap created, all 20 requirements mapped to 5 phases
Resume file: None
