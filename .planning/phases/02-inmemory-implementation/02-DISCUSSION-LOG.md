# Phase 2: InMemory Implementation - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-05-03
**Phase:** 02-inmemory-implementation
**Areas discussed:** Semaphore scope, UpsertSchedule locking, Plain Dictionary safety, Test structure

---

## Semaphore Scope

| Option | Description | Selected |
|--------|-------------|----------|
| Instance field | `ConcurrentDictionary<QueueKey, SemaphoreSlim>` as a private field on `InMemoryStorage`. Each instance isolated — fixes the deferred test isolation bug. | ✓ |
| Static dictionary | Keep the static `ConcurrentDictionary` pattern from `InMemoryLeasingScopeFactory`. Process-wide sharing — preserves the known test isolation bug. | |

**User's choice:** Instance field (Recommended)
**Notes:** None — clear choice.

---

### When semaphore not acquired

| Option | Description | Selected |
|--------|-------------|----------|
| Return empty / no-op | `WaitAsync(TimeSpan.Zero)`. If not acquired, return `default(TResult)` / complete void overload without calling callback. Mirrors old `Acquired=false` behavior. | ✓ |
| Wait and retry | Block with `WaitAsync(cancellationToken)` until acquired. Could back up pollers on high-contention queues; diverges from `SKIP LOCKED` intent. | |

**User's choice:** Return empty / no-op (Recommended)
**Notes:** None.

---

### Non-generic overload implementation

| Option | Description | Selected |
|--------|-------------|----------|
| Delegate to generic | `ExecuteInLeaseAsync<bool>(queue, async ct => { await callback(ct); return true; }, ct)`. Single lock path, no duplication. | ✓ |
| Separate implementation | Duplicate the `WaitAsync / try / finally` pattern. Two lock paths to maintain, no benefit. | |

**User's choice:** Delegate to generic (Recommended)
**Notes:** None.

---

## UpsertSchedule Locking

| Option | Description | Selected |
|--------|-------------|----------|
| Dedicated schedule lock | Separate `SemaphoreSlim _scheduleLock(1,1)`. `UpsertScheduleAsync` and `ExecuteInLeaseAsync` for `QueueKey.Scheduler` both use it. Keeps schedule writes isolated from job queue lanes. | ✓ |
| Reuse QueueKey.Scheduler semaphore | Use per-queue semaphore for `QueueKey.Scheduler` for everything including `UpsertScheduleAsync`. Couples write path to polling internals. | |

**User's choice:** Dedicated schedule lock (Recommended)
**Notes:** None.

---

### UpsertScheduleAsync lock acquisition

| Option | Description | Selected |
|--------|-------------|----------|
| Always wait | `WaitAsync(cancellationToken)`. Block until lock available. InMemory is single-process; wait resolves in microseconds. | ✓ |
| Try zero-timeout, throw on miss | `WaitAsync(TimeSpan.Zero)` and throw if not acquired. Could surface false failures. | |

**User's choice:** Always wait
**Notes:** User initially asked about distributed environments — clarified that `InMemoryStorage` is single-process only. Multiple service replicas each get their own isolated `InMemoryStorage`; cross-process coordination is the EF Core concern (Phase 4). With that understood, always-wait is correct.

---

## Plain Dictionary Safety

| Option | Description | Selected |
|--------|-------------|----------|
| Lock wraps all access | Leave `_queues` as `Dictionary` but guard all access via the per-queue semaphore. Locks do double-duty. | |
| Convert to ConcurrentDictionary | Replace outer `Dictionary` and inner `HashSet`. `ConcurrentDictionary` doesn't protect inner `HashSet` — net complexity same or higher. | |

**User's choice:** No lock wraps all access — went further (see below)
**Notes:** First question was about outer dictionary; user pushed back on locking `InsertAsync` for perf reasons.

---

### InsertAsync thread safety

| Option | Description | Selected |
|--------|-------------|----------|
| Don't lock InsertAsync — pre-existing issue | InsertAsync stays lock-free. Per-queue semaphore guards only polling path. HashSet race is a pre-existing limitation. | |
| Change inner HashSet to ConcurrentDictionary<Guid, byte> | Replace `HashSet<Guid>` with `ConcurrentDictionary<Guid, byte>`. Eliminates enumeration race without adding a lock to `InsertAsync`. | ✓ |

**User's choice:** Change inner `HashSet` to `ConcurrentDictionary<Guid, byte>`
**Notes:** User correctly identified that locking `InsertAsync` was unnecessary overhead. Claude explained the actual race (concurrent `Add` + `foreach` on plain `HashSet`). User chose the surgical fix: concurrent set type eliminates the race without any lock in `InsertAsync`.

---

## Test Structure

| Option | Description | Selected |
|--------|-------------|----------|
| New file: InMemoryStorageLeaseTests.cs | Separate test class for lease-specific behavior. Existing `InMemoryStorageTests.cs` stays focused on CRUD. | ✓ |
| Append to InMemoryStorageTests.cs | Add lease tests to the existing file. Simpler but the file would grow long and mix concerns. | |

**User's choice:** New file: `InMemoryStorageLeaseTests.cs` (Recommended)
**Notes:** None.

---

### InMemoryLeasingScopeFactoryTests.cs fate

| Option | Description | Selected |
|--------|-------------|----------|
| Yes — delete in Phase 2 | Remove the file; it tests a deleted class and won't compile. | ✓ |
| It was already deleted in Phase 1 | Phase 1 plan 01-04-PLAN.md may have covered it already. | |

**User's choice:** Yes — delete in Phase 2
**Notes:** Verify at implementation time whether Phase 1 already removed it; if so, this is a no-op.

---

## Claude's Discretion

- Whether `_scheduleLock` is a separate `SemaphoreSlim` field or stored under `QueueKey.Scheduler` in `_semaphores` — both valid; planner picks the cleaner option.
- Exact logging messages and log levels for the semaphore-not-acquired path.
- Whether `EvictCompletedAndFailed` needs any guard given `_jobs` is already `ConcurrentDictionary`.

## Deferred Ideas

None — discussion stayed within phase scope.
