# Requirements: Atomizer — Storage Refactor

**Defined:** 2026-05-03
**Core Value:** A storage abstraction so clean and correct that implementing a new backend requires no tribal knowledge — just the interface.

## v1 Requirements

### Leasing Abstraction

- [ ] **LEASE-01**: Storage consumers call a single `ExecuteInLeaseAsync(queue, callback)` method — the open transaction/lock is managed inside the callback, not by the caller
- [ ] **LEASE-02**: `IAtomizerLeasingScopeFactory` and `IAtomizerLeasingScope` are removed from the public API; `Acquired` flag is gone — a lease either succeeds or throws/returns empty
- [ ] **LEASE-03**: The leasing contract does not assume SQL transactions — each backend decides how to implement atomicity (transaction, SemaphoreSlim, distributed lock, optimistic retry)

### Atomic Acquisition

- [ ] **ACQR-01**: EF Core `GetDueJobsAsync` acquires rows using `FOR UPDATE SKIP LOCKED` (PostgreSQL/MySQL) or `WITH (UPDLOCK, ROWLOCK, READPAST)` (SQL Server) inside the active lease transaction
- [ ] **ACQR-02**: EF Core `GetDueSchedulesAsync` uses the same provider-appropriate row-locking inside the lease transaction
- [ ] **ACQR-03**: Row locks are held until `UpdateJobsAsync` / `UpdateSchedulesAsync` commits — double-dispatch is structurally impossible within a single lease callback

### Schedule Upsert

- [ ] **UPSRT-01**: `UpsertScheduleAsync` for PostgreSQL uses `INSERT ... ON CONFLICT (job_key) DO UPDATE SET ...` — atomic, no race condition
- [ ] **UPSRT-02**: `UpsertScheduleAsync` for SQL Server uses `MERGE ... USING ... WHEN MATCHED THEN UPDATE WHEN NOT MATCHED THEN INSERT`
- [ ] **UPSRT-03**: `UpsertScheduleAsync` for MySQL uses `INSERT ... ON DUPLICATE KEY UPDATE ...`
- [ ] **UPSRT-04**: The existing `@todo` race condition (check-then-insert) is eliminated across all supported providers

### SQL Dialect Strategy

- [ ] **DIAL-01**: A `ISqlDialect` interface (or equivalent) is introduced — exposes methods for due-job acquisition SQL and schedule upsert SQL
- [ ] **DIAL-02**: `PostgreSqlDialect`, `SqlServerDialect`, and `MySqlDialect` implement `ISqlDialect` — each encapsulates all provider-specific SQL for the storage layer
- [ ] **DIAL-03**: EF Core storage class contains no inline provider-branching SQL — it delegates all raw SQL to the injected dialect
- [ ] **DIAL-04**: Adding a new relational provider requires only implementing `ISqlDialect` and registering it — no changes to the storage class itself

### InMemory Alignment

- [x] **INMEM-01**: InMemory backend implements the same callback-based leasing contract as EF Core — callers cannot observe behavioral differences
- [x] **INMEM-02**: InMemory `GetDueJobsAsync` holds its per-queue `SemaphoreSlim` lock for the duration of the lease callback (released after the callback completes, same as EF Core transaction lifecycle)
- [x] **INMEM-03**: InMemory `UpsertScheduleAsync` is atomic — uses the existing lock to prevent the same race condition as the SQL @todo

### Public API & Versioning

- [ ] **COMPAT-01**: `IAtomizerStorage` interface is updated to reflect the new leasing contract — this is a documented breaking change
- [ ] **COMPAT-02**: A major version bump is applied to both `Atomizer` and `Atomizer.EntityFrameworkCore` NuGet packages
- [ ] **COMPAT-03**: XML documentation on all new/changed public API members

## v2 Requirements

### Future Providers

- **PROV-01**: MongoDB backend implementing `ExecuteInLeaseAsync` via distributed lock (e.g., MongoDB change streams or TTL-based lock document)
- **PROV-02**: Redis backend implementing `ExecuteInLeaseAsync` via RedLock or Lua script atomicity
- **PROV-03**: SQLite promoted to a production-supported provider (requires workaround for lack of `SKIP LOCKED`)

### Observability

- **OBS-01**: Lease acquisition metrics (time-to-acquire, contention rate) exposed via `IAtomizerMetrics`
- **OBS-02**: Dialect-level query telemetry (query execution time per provider)

## Out of Scope

| Feature | Reason |
|---------|--------|
| MongoDB / Redis backend implementations | This milestone defines the abstraction; backends are future milestones |
| SQLite as production-supported provider | Lacks `FOR NO KEY UPDATE SKIP LOCKED`; stays test-only |
| Changes to `IAtomizerClient` public API | Out of scope — storage refactor only |
| Changes to processing pipeline (QueuePump, JobWorker, DefaultJobDispatcher) | Out of scope |
| Entity schema changes (table layout, column names) | Out of scope unless required by upsert SQL |
| AtomizerJobErrorEntity redesign | Deferred — separate concern |
| Oracle provider | Removed in #10 — not being re-added |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| LEASE-01 | Phase 1 | Pending |
| LEASE-02 | Phase 1 | Pending |
| LEASE-03 | Phase 1 | Pending |
| COMPAT-01 | Phase 1 | Pending |
| INMEM-01 | Phase 2 | Complete |
| INMEM-02 | Phase 2 | Complete |
| INMEM-03 | Phase 2 | Complete |
| DIAL-01 | Phase 3 | Pending |
| DIAL-02 | Phase 3 | Pending |
| DIAL-03 | Phase 3 | Pending |
| DIAL-04 | Phase 3 | Pending |
| ACQR-01 | Phase 4 | Pending |
| ACQR-02 | Phase 4 | Pending |
| ACQR-03 | Phase 4 | Pending |
| UPSRT-01 | Phase 4 | Pending |
| UPSRT-02 | Phase 4 | Pending |
| UPSRT-03 | Phase 4 | Pending |
| UPSRT-04 | Phase 4 | Pending |
| COMPAT-02 | Phase 5 | Pending |
| COMPAT-03 | Phase 5 | Pending |

**Coverage:**
- v1 requirements: 20 total
- Mapped to phases: 20 ✓
- Unmapped: 0

---
*Requirements defined: 2026-05-03*
*Last updated: 2026-05-03 after roadmap creation — all 20 requirements mapped*
