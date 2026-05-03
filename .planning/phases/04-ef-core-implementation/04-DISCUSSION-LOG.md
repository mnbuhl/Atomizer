# Phase 4: EF Core Implementation - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-05-03
**Phase:** 4-EF Core Implementation
**Areas discussed:** Transaction ownership, Upsert fallback for SQLite, AsNoTracking + UpdateRange within transaction, Unique index on job_key

---

## Transaction Ownership

### How to manage the ReadCommitted transaction

| Option | Description | Selected |
|--------|-------------|----------|
| Reuse existing DatabaseTransactionLeasingScope | Call StartTransaction(...) inside ExecuteInLeaseAsync, run callback inside it, commit/rollback via DisposeAsync() | ✓ |
| Inline transaction logic | Inline BeginTransactionAsync, try/commit, catch/rollback directly in ExecuteInLeaseAsync | |

**User's choice:** Reuse existing scope (Recommended)
**Notes:** Avoids duplicating proven logic; Phase 5 still deletes the class after storage no longer needs it.

---

### Lock timeout source

| Option | Description | Selected |
|--------|-------------|----------|
| EntityFrameworkCoreJobStorageOptions | Add LockTimeout property (default 30s); ExecuteInLeaseAsync reads _options.LockTimeout | ✓ |
| Hardcoded 30s | Use TimeSpan.FromSeconds(30) directly — not configurable | |
| QueueOptions.VisibilityTimeout | Pass from caller — would change the Phase 1 interface contract | |

**User's choice:** EntityFrameworkCoreJobStorageOptions (Recommended)
**Notes:** Consistent with how the EF Core storage reads other options.

---

### Behavior when lease acquisition fails (Acquired = false)

| Option | Description | Selected |
|--------|-------------|----------|
| Return empty / skip | Return Task.CompletedTask or default(TResult) — poller skips tick and retries | ✓ |
| Throw typed exception | Throw AtomizerLeaseAcquisitionException — callers must catch it | |

**User's choice:** Return empty / skip (Recommended)
**Notes:** Preserves the same semantics as the old Acquired=false behavior — poller silently skips the tick.

---

## Upsert Fallback for SQLite

| Option | Description | Selected |
|--------|-------------|----------|
| Leave as-is — comment it | Keep EF check-then-insert for fallback path; add comment noting it's not race-safe and test-only | ✓ |
| SQLite INSERT OR REPLACE | Add SQLite-specific safe path in fallback branch | |
| Keep NotImplementedException | Stub fallback so SQLite tests fail visibly on schedule upsert | |

**User's choice:** Leave as-is — comment it (Recommended)
**Notes:** SQLite is test-only; the race condition is acceptable for test isolation scenarios. A clear comment communicates the limitation.

---

## AsNoTracking + UpdateRange Within Transaction

### Whether to remove AsNoTracking from GetDueJobsAsync

| Option | Description | Selected |
|--------|-------------|----------|
| Keep AsNoTracking — UpdateRange is fine | Standard pattern; EF re-attaches on UpdateRange regardless of tracking | |
| Remove AsNoTracking on GetDueJobsAsync | Track entities from fetch; EF detects changes automatically | ✓ |

**User's choice:** Remove AsNoTracking on GetDueJobsAsync
**Notes:** More idiomatic EF Core — tracked entities inside the lease transaction.

---

### Scope of the tracking change

| Option | Description | Selected |
|--------|-------------|----------|
| UpdateRange everywhere (simplest) | Remove AsNoTracking from GetDueJobsAsync; UpdateJobsAsync keeps UpdateRange for all callers | ✓ |
| SaveChanges inside lease, UpdateRange outside | Different paths depending on whether caller is inside lease or not | |

**User's choice:** UpdateRange everywhere (simplest)
**Notes:** Avoids callers needing to know which code path they're on.

---

## Unique Index on job_key

### Discovery: no unique index exists

Checked `AtomizerScheduleEntityConfiguration.cs` and SQL Server migration `20250827155953_Initial.cs` — confirmed no unique index on `JobKey` in the schedules table today.

### How to add the unique index

| Option | Description | Selected |
|--------|-------------|----------|
| HasIndex in EF config + new migrations | Add .HasIndex(e => e.JobKey).IsUnique() in config; generate migrations via dotnet ef | |
| Raw SQL migration only | Hand-write migration with CREATE UNIQUE INDEX; skip EF config change | |

**User's choice:** HasIndex in EF config + new migrations (initial preference)
**Notes:** User then reconsidered migration approach entirely.

---

### Migration strategy: MigrateAsync vs EnsureCreatedAsync

| Option | Description | Selected |
|--------|-------------|----------|
| Delete migrations, use EnsureCreatedAsync | BaseDatabaseFixture calls EnsureCreatedAsync(); all migration folders and DesignTimeDbContextFactory classes deleted from test project | ✓ |
| Keep migrations, add a new one | Add migration for unique index; preserve migration-based test approach | |

**User's choice:** Yes — delete migrations, use EnsureCreatedAsync (Recommended)
**Notes:** Testcontainers always starts a fresh database; no migration history needed. EF config change (HasIndex) is immediately reflected without generating or maintaining migration files. Removes migration noise from all testing and sample folders.

---

## Claude's Discretion

- Exact property name for lock timeout on `EntityFrameworkCoreJobStorageOptions` (`LockTimeout`, `LockAcquisitionTimeout`, `TransactionTimeout` — all acceptable)
- Whether `AsNoTracking()` is removed only inside the lease callback path or globally from `GetDueJobsAsync` (simpler to remove globally)
- Exact column name used in `ON CONFLICT (job_key)` — must match actual column name from `EntityMap`
- XML documentation wording for the new `LockTimeout` property

## Deferred Ideas

None — discussion stayed within phase scope.
