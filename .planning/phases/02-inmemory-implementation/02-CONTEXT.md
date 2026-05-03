# Phase 2: InMemory Implementation - Context

**Gathered:** 2026-05-03
**Status:** Ready for planning

<domain>
## Phase Boundary

Replace the two `NotImplementedException` stubs in `InMemoryStorage.ExecuteInLeaseAsync` with real implementations, make `UpsertScheduleAsync` atomic via a dedicated schedule lock, fix the inner-collection thread-safety issue in `_queues`, and add lease-specific tests. Phase 2 is InMemory-only — no EF Core, no public API changes, no processing pipeline changes.

</domain>

<decisions>
## Implementation Decisions

### Semaphore Scope and Acquisition

- **D-01:** Per-queue `SemaphoreSlim` instances live as an **instance field** on `InMemoryStorage` — `private readonly ConcurrentDictionary<QueueKey, SemaphoreSlim> _semaphores`. Each `InMemoryStorage` instance is isolated. Fixes the deferred test isolation bug (previously static in `InMemoryLeasingScopeFactory`).
- **D-02:** If the semaphore cannot be acquired (`WaitAsync(TimeSpan.Zero)` returns false), `ExecuteInLeaseAsync` **returns early without invoking the callback** — returns `default(TResult)` for the generic overload, completes for the void overload. Mirrors the old `Acquired=false` skip-this-tick behavior; consistent with EF Core's `SKIP LOCKED` intent.
- **D-03:** The non-generic overload **delegates to the generic overload**: `ExecuteInLeaseAsync<bool>(queue, async ct => { await callback(ct); return true; }, ct)`. Single lock path, no duplication.
- **D-04:** Callback exception → release semaphore (finally block) and rethrow unchanged (carried from Phase 1 D-04).

### UpsertSchedule Locking

- **D-05:** A **dedicated `SemaphoreSlim _scheduleLock = new(1, 1)`** instance field guards all schedule writes. `UpsertScheduleAsync` acquires `_scheduleLock` before reading/writing `_schedules`. `ExecuteInLeaseAsync` for `QueueKey.Scheduler` acquires `_scheduleLock` as its semaphore (looked up via the same `_semaphores` dictionary, or a special-cased path — planner's choice).
- **D-06:** `UpsertScheduleAsync` uses `WaitAsync(cancellationToken)` — **always waits** until the lock is available. InMemory is single-process; the lock is held for microseconds (pure in-memory ops). No skip-on-busy semantics needed for a client write call.

### Inner Collection Thread-Safety

- **D-07:** Replace `Dictionary<QueueKey, HashSet<Guid>> _queues` inner `HashSet<Guid>` with `ConcurrentDictionary<Guid, byte>`. `_queues` outer becomes `ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>>`. This eliminates the `InvalidOperationException: collection was modified` race between `InsertAsync` (no lock) and `GetDueJobsAsync` (inside lease semaphore) without adding a lock to `InsertAsync`.
- **D-08:** `_schedules` stays as `Dictionary<JobKey, AtomizerSchedule>` — all access goes through `_scheduleLock` so a plain dictionary is safe.

### Test Structure

- **D-09:** Lease-specific tests go in a **new file** `tests/Atomizer.Tests/Storage/InMemoryStorageLeaseTests.cs`. Existing `InMemoryStorageTests.cs` stays focused on CRUD methods. The new file covers: semaphore held for full callback duration, concurrent callers serialized, exception releases semaphore, non-acquired case returns empty.
- **D-10:** `tests/Atomizer.Tests/Storage/InMemoryLeasingScopeFactoryTests.cs` is **deleted in Phase 2** (tests a class removed in Phase 1; file won't compile).

### Claude's Discretion

- Whether `_scheduleLock` is a separate `SemaphoreSlim` field or stored under `QueueKey.Scheduler` in `_semaphores` — both are valid; planner picks whichever is cleaner.
- Exact logging messages and log levels for semaphore-not-acquired path.
- Whether `EvictCompletedAndFailed` needs any guard given the `_jobs` `ConcurrentDictionary` is already concurrent.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Requirements Governing This Phase
- `.planning/REQUIREMENTS.md` — Requirements INMEM-01, INMEM-02, INMEM-03 govern Phase 2
- `.planning/ROADMAP.md` — Phase 2 success criteria (4 items) are the acceptance test

### Storage Files Being Changed
- `src/Atomizer/Storage/InMemoryStorage.cs` — Primary target: implement `ExecuteInLeaseAsync` overloads, add `_semaphores` and `_scheduleLock` fields, change inner collection type, make `UpsertScheduleAsync` atomic
- `src/Atomizer/Abstractions/IAtomizerStorage.cs` — Interface shape already finalized in Phase 1; read for reference only

### Files Being Deleted
- `tests/Atomizer.Tests/Storage/InMemoryLeasingScopeFactoryTests.cs` — Tests a deleted class; remove in this phase

### Test Files
- `tests/Atomizer.Tests/Storage/InMemoryStorageTests.cs` — Existing tests; keep passing, update if inner collection type change requires test spy adjustments
- `tests/Atomizer.Tests/Storage/InMemoryStorageLeaseTests.cs` — New file to create

### Phase 1 Context (locked decisions)
- `.planning/phases/01-leasing-abstraction/01-CONTEXT.md` — D-04 (exception rollback), D-07/D-08 (ReleaseLeasedAsync unchanged, LeaseToken per-pump), D-03 (caller invokes GetDueJobsAsync inside callback)

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `InMemoryStorage._jobs` — `ConcurrentDictionary<Guid, AtomizerJob>` — already thread-safe; no changes needed
- `InMemoryStorage._leasesByToken` — `ConcurrentDictionary<string, ConcurrentDictionary<Guid, byte>>` — already thread-safe; no changes needed
- `InMemoryStorage.UpdateLease` / `ReleaseLeasedAsync` — survive unchanged; `ReleaseLeasedAsync` is the shutdown recovery path (Phase 1 D-07)
- `InMemoryLeasingScopeFactory.InMemoryLeasingScope` — the `try/finally` semaphore release pattern there is the model to replicate inside `ExecuteInLeaseAsync`
- `QueuePoller.RunAsync` — calls `storage.ExecuteInLeaseAsync` with a callback that calls `GetDueJobsAsync` + `UpdateJobsAsync`; read to confirm the call site shape Phase 2 must satisfy

### Established Patterns
- `#if NETCOREAPP3_0_OR_GREATER` guard — not needed for `ExecuteInLeaseAsync` (returns `Task`, not `IAsyncDisposable`)
- `CancellationToken.ThrowIfCancellationRequested()` at the top of sync-path methods — follow existing style
- `internal sealed class` for `InMemoryStorage` — already `public sealed`; no change
- `SemaphoreSlim(1, 1)` — standard single-permit mutex pattern already established by `InMemoryLeasingScopeFactory`

### Integration Points
- `QueuePoller` calls `scope.Storage.ExecuteInLeaseAsync(queue.QueueKey, callback, ct)` — Phase 2 must make the InMemory path work end-to-end for the in-memory integration test suite
- `SchedulePoller` calls `scope.Storage.ExecuteInLeaseAsync(QueueKey.Scheduler, callback, ct)` — same requirement
- `AtomizerClient.ScheduleRecurringAsync` calls `storage.UpsertScheduleAsync` outside any lease — Phase 2 must make this safe concurrently with the scheduler polling path

</code_context>

<specifics>
## Specific Ideas

- Non-generic overload delegates to generic:
  ```csharp
  public Task ExecuteInLeaseAsync(
      QueueKey queue,
      Func<CancellationToken, Task> callback,
      CancellationToken cancellationToken
  ) => ExecuteInLeaseAsync<bool>(queue, async ct => { await callback(ct); return true; }, cancellationToken);
  ```

- Inner queue set type change:
  ```csharp
  // Before
  private readonly Dictionary<QueueKey, HashSet<Guid>> _queues = new();
  // After
  private readonly ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>> _queues = new();
  ```

- Semaphore acquire pattern (try-zero-timeout, no-op on miss):
  ```csharp
  var semaphore = _semaphores.GetOrAdd(queue, _ => new SemaphoreSlim(1, 1));
  var acquired = await semaphore.WaitAsync(TimeSpan.Zero, cancellationToken);
  if (!acquired)
      return default!;
  try
  {
      return await callback(cancellationToken);
  }
  finally
  {
      semaphore.Release();
  }
  ```

</specifics>

<deferred>
## Deferred Ideas

- `_scheduleLock` / `_semaphores` merging into a single abstraction — planner's call in Phase 2; no user preference expressed
- Thread-safety of `InsertAsync` against `_queues` outer dictionary add — `ConcurrentDictionary` for the outer handles the structural race; noted as pre-existing limitation beyond INMEM scope

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 2-InMemory Implementation*
*Context gathered: 2026-05-03*
