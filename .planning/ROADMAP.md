# Roadmap: Atomizer — Storage Refactor

## Overview

This milestone refactors the Atomizer storage layer from a separate leasing-scope abstraction to a unified callback-based `ExecuteInLeaseAsync` contract. The work proceeds in strict dependency order: define the contract, validate it on the simpler InMemory backend, extract provider SQL into dialect classes, then implement the full EF Core backend with atomic acquisition and native per-provider upsert. The milestone closes by removing all deprecated types and shipping a major version bump.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [ ] **Phase 1: Leasing Abstraction** - Define `ExecuteInLeaseAsync` contract and update `IAtomizerStorage` as a documented breaking change
- [ ] **Phase 2: InMemory Implementation** - Align InMemory backend to the new callback-based leasing contract
- [ ] **Phase 3: SQL Dialect Strategy** - Extract `ISqlDialect` per-provider strategy classes from inline EF Core SQL branching
- [ ] **Phase 4: EF Core Implementation** - Implement callback-based leasing, atomic row-locked acquisition, and native upsert in EF Core storage
- [ ] **Phase 5: Cleanup and Versioning** - Remove deprecated types, apply major version bump, complete XML documentation

## Phase Details

### Phase 1: Leasing Abstraction
**Goal**: The new `ExecuteInLeaseAsync` contract exists as a clean, provider-agnostic interface and `IAtomizerStorage` is updated to reflect it
**Depends on**: Nothing (first phase)
**Requirements**: LEASE-01, LEASE-02, LEASE-03, COMPAT-01
**Success Criteria** (what must be TRUE):
  1. `IAtomizerStorage` exposes `ExecuteInLeaseAsync(queue, callback, ct)` — callers pass work as a callback, not as a two-step acquire/release pattern
  2. `IAtomizerLeasingScopeFactory`, `IAtomizerLeasingScope`, and the `Acquired` flag are removed from the public API surface
  3. The interface signature contains no SQL or transaction primitives — each backend decides how to implement atomicity
  4. The existing `QueuePoller` and `SchedulePoller` callers are updated to use the new call site
**Plans**: TBD

### Phase 2: InMemory Implementation
**Goal**: The InMemory backend implements the new callback-based leasing contract with the same atomicity guarantees as EF Core from the caller's perspective
**Depends on**: Phase 1
**Requirements**: INMEM-01, INMEM-02, INMEM-03
**Success Criteria** (what must be TRUE):
  1. `InMemoryStorage.ExecuteInLeaseAsync` holds the per-queue `SemaphoreSlim` for the entire duration of the callback and releases it only after the callback completes
  2. `InMemoryStorage.UpsertScheduleAsync` is atomic — the existing per-queue lock prevents the same race condition fixed on EF Core
  3. The InMemory unit test suite passes without the `IAtomizerLeasingScopeFactory` dependency
  4. A caller using the InMemory backend cannot observe a behavioral difference in error modes or lock lifecycle compared to the EF Core contract
**Plans**: TBD

### Phase 3: SQL Dialect Strategy
**Goal**: All provider-specific SQL is extracted into `ISqlDialect` strategy classes — the EF Core storage class contains no inline provider branching
**Depends on**: Phase 1
**Requirements**: DIAL-01, DIAL-02, DIAL-03, DIAL-04
**Success Criteria** (what must be TRUE):
  1. An `ISqlDialect` interface (or equivalent) exists with methods covering due-job acquisition SQL and schedule upsert SQL
  2. `PostgreSqlDialect`, `SqlServerDialect`, and `MySqlDialect` each implement `ISqlDialect` and own all provider-specific SQL for the storage layer
  3. `EntityFrameworkCoreStorage` contains no `if/switch` on provider type — it delegates all raw SQL to the injected dialect
  4. Adding a new relational provider requires only implementing `ISqlDialect` and registering it — no changes to the storage class
**Plans**: TBD

### Phase 4: EF Core Implementation
**Goal**: EF Core storage implements callback-based leasing with row-locked atomic acquisition and native per-provider upsert, eliminating the schedule upsert race condition
**Depends on**: Phase 3
**Requirements**: ACQR-01, ACQR-02, ACQR-03, UPSRT-01, UPSRT-02, UPSRT-03, UPSRT-04
**Success Criteria** (what must be TRUE):
  1. `EntityFrameworkCoreStorage.ExecuteInLeaseAsync` opens a `ReadCommitted` transaction that spans the full callback — `GetDueJobsAsync` acquires row locks and `UpdateJobsAsync` commits within the same transaction
  2. `GetDueJobsAsync` uses `FOR UPDATE SKIP LOCKED` (PostgreSQL/MySQL) or `WITH (UPDLOCK, ROWLOCK, READPAST)` (SQL Server) — double-dispatch is structurally impossible within one lease callback
  3. `GetDueSchedulesAsync` uses the same provider-appropriate row locking inside the lease transaction
  4. `UpsertScheduleAsync` for PostgreSQL uses `INSERT ... ON CONFLICT (job_key) DO UPDATE SET ...`
  5. `UpsertScheduleAsync` for SQL Server uses `MERGE ... USING ... WHEN MATCHED THEN UPDATE WHEN NOT MATCHED THEN INSERT`
  6. `UpsertScheduleAsync` for MySQL uses `INSERT ... ON DUPLICATE KEY UPDATE ...`
  7. The integration test suite passes on all three provider containers (PostgreSQL, SQL Server, MySQL) with no concurrent-upsert failures
**Plans**: TBD

### Phase 5: Cleanup and Versioning
**Goal**: All deprecated leasing types are removed from both packages, a major version bump is applied, and all new/changed public API members carry XML documentation
**Depends on**: Phase 4
**Requirements**: COMPAT-02, COMPAT-03
**Success Criteria** (what must be TRUE):
  1. `IAtomizerLeasingScopeFactory`, `IAtomizerLeasingScope`, `DatabaseTransactionLeasingScopeFactory`, `InMemoryLeasingScopeFactory`, `NoopLeasingScopeFactory`, and `LeasingScopeOptions` are absent from both shipped assemblies
  2. Both `Atomizer` and `Atomizer.EntityFrameworkCore` NuGet packages carry a major version number higher than the last published version
  3. Every new or changed public API member has an XML `<summary>` (and `<param>` / `<returns>` where applicable) — the build passes with `TreatWarningsAsErrors=true`
**Plans**: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 1 → 2 → 3 → 4 → 5

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Leasing Abstraction | 0/TBD | Not started | - |
| 2. InMemory Implementation | 0/TBD | Not started | - |
| 3. SQL Dialect Strategy | 0/TBD | Not started | - |
| 4. EF Core Implementation | 0/TBD | Not started | - |
| 5. Cleanup and Versioning | 0/TBD | Not started | - |
