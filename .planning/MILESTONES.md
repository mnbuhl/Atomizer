# Milestones

## v1.0 — Storage Refactor ✅ SHIPPED 2026-05-03

**Phases:** 5 | **Plans:** 17 | **Commits:** 227
**Files:** 253 changed (+33,518 / -215) | **Source LOC:** ~6,020 C#
**Timeline:** 2025-08-09 → 2026-05-03

### Delivered

Refactored the Atomizer storage layer from a two-step acquire/release leasing model to a unified callback-based `ExecuteInLeaseAsync` contract — making backends self-contained, atomicity provider-agnostic, and correct by construction.

### Key Accomplishments

1. **Callback-based leasing contract** — `IAtomizerStorage.ExecuteInLeaseAsync` replaces `IAtomizerLeasingScopeFactory`/`IAtomizerLeasingScope`; lock lifecycle is now inside the backend
2. **InMemory backend aligned** — per-queue `SemaphoreSlim` held for full callback; `UpsertScheduleAsync` atomic via scheduler semaphore
3. **ISqlDialect strategy pattern** — `IDatabaseProviderSql` replaced by `internal ISqlDialect`; `PostgreSqlDialect`, `SqlServerDialect`, `MySqlDialect` own all provider SQL; zero inline branching in storage
4. **EF Core atomic acquisition** — `ReadCommitted` transaction spans `GetDueJobsAsync` + `UpdateJobsAsync`; `FOR UPDATE SKIP LOCKED` / `WITH (UPDLOCK, READPAST, ROWLOCK)` prevent double-dispatch
5. **Native upsert per provider** — `ON CONFLICT ... DO UPDATE` / `MERGE ... WITH (HOLDLOCK)` / `INSERT ... ON DUPLICATE KEY UPDATE`; eliminates @todo race condition
6. **Full XML documentation** — `GenerateDocumentationFile=true`; zero CS1591 under `TreatWarningsAsErrors=true`

### Known Deferred Items at Close: 7 (see STATE.md Deferred Items)

**Archive:** [v1.0-ROADMAP.md](milestones/v1.0-ROADMAP.md) | [v1.0-REQUIREMENTS.md](milestones/v1.0-REQUIREMENTS.md)
