# Phase 2: InMemory Implementation - Research

**Researched:** 2026-05-03
**Domain:** C# / .NET in-process concurrency — `SemaphoreSlim`, `ConcurrentDictionary`, callback-based leasing
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**D-01:** Per-queue `SemaphoreSlim` instances live as an **instance field** on `InMemoryStorage` —
`private readonly ConcurrentDictionary<QueueKey, SemaphoreSlim> _semaphores`. Each `InMemoryStorage`
instance is isolated. Fixes the deferred test isolation bug (previously static in `InMemoryLeasingScopeFactory`).

**D-02:** If the semaphore cannot be acquired (`WaitAsync(TimeSpan.Zero)` returns false),
`ExecuteInLeaseAsync` **returns early without invoking the callback** — returns `default(TResult)` for the
generic overload, completes for the void overload.

**D-03:** The non-generic overload **delegates to the generic overload**:
`ExecuteInLeaseAsync<bool>(queue, async ct => { await callback(ct); return true; }, ct)`. Single lock path,
no duplication.

**D-04:** Callback exception → release semaphore (`finally` block) and rethrow unchanged (carried from
Phase 1 D-04).

**D-05:** A **dedicated `SemaphoreSlim _scheduleLock = new(1, 1)`** instance field guards all schedule
writes. `UpsertScheduleAsync` acquires `_scheduleLock` before reading/writing `_schedules`.
`ExecuteInLeaseAsync` for `QueueKey.Scheduler` acquires `_scheduleLock` as its semaphore (either via the
same `_semaphores` dictionary or a special-cased path — planner's choice).

**D-06:** `UpsertScheduleAsync` uses `WaitAsync(cancellationToken)` — **always waits** until the lock is
available. No skip-on-busy semantics for a client write call.

**D-07:** Replace `Dictionary<QueueKey, HashSet<Guid>> _queues` inner `HashSet<Guid>` with
`ConcurrentDictionary<Guid, byte>`. `_queues` outer becomes
`ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>>`. This eliminates the
`InvalidOperationException: collection was modified` race between `InsertAsync` and `GetDueJobsAsync`.

**D-08:** `_schedules` stays as `Dictionary<JobKey, AtomizerSchedule>` — all access goes through
`_scheduleLock` so a plain dictionary is safe.

**D-09:** Lease-specific tests go in a **new file**
`tests/Atomizer.Tests/Storage/InMemoryStorageLeaseTests.cs`. Existing `InMemoryStorageTests.cs` stays
focused on CRUD methods.

**D-10:** `tests/Atomizer.Tests/Storage/InMemoryLeasingScopeFactoryTests.cs` is **deleted in Phase 2**.

### Claude's Discretion

- Whether `_scheduleLock` is a separate `SemaphoreSlim` field or stored under `QueueKey.Scheduler` in
  `_semaphores` — both are valid; planner picks whichever is cleaner.
- Exact logging messages and log levels for semaphore-not-acquired path.
- Whether `EvictCompletedAndFailed` needs any guard given the `_jobs` `ConcurrentDictionary` is already
  concurrent.

### Deferred Ideas (OUT OF SCOPE)

- `_scheduleLock` / `_semaphores` merging into a single abstraction — planner's call in Phase 2; no user
  preference expressed.
- Thread-safety of `InsertAsync` against `_queues` outer dictionary add — `ConcurrentDictionary` for the
  outer handles the structural race; noted as pre-existing limitation beyond INMEM scope.
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| INMEM-01 | InMemory backend implements the same callback-based leasing contract as EF Core — callers cannot observe behavioral differences | Verified: `IAtomizerStorage.ExecuteInLeaseAsync` interface is finalized in Phase 1; InMemory stubs throw `NotImplementedException`. Implementation pattern from `InMemoryLeasingScopeFactory.InMemoryLeasingScope` is directly reusable. |
| INMEM-02 | InMemory `GetDueJobsAsync` holds its per-queue `SemaphoreSlim` lock for the duration of the lease callback (released after the callback completes, same as EF Core transaction lifecycle) | Verified: `SemaphoreSlim.WaitAsync` + `try/finally` Release is the established pattern in `InMemoryLeasingScopeFactory`. The callback model maps cleanly to wrapping the `await callback(ct)` call inside the held semaphore. |
| INMEM-03 | InMemory `UpsertScheduleAsync` is atomic — uses the existing lock to prevent the same race condition as the SQL @todo | Verified: `_schedules` is currently an unguarded `Dictionary<JobKey, AtomizerSchedule>`. Adding `_scheduleLock.WaitAsync(ct)` before read/write makes it safe. `UpdateSchedulesAsync` is called only from inside a lease callback so it is already serialized; `UpsertScheduleAsync` is called outside any lease and needs its own guard. |
</phase_requirements>

---

## Summary

Phase 2 is a surgical, self-contained change to a single source file (`InMemoryStorage.cs`), one test file
deletion, and one new test file. The interface contract (`IAtomizerStorage`) was finalized in Phase 1 and is
read-only from Phase 2's perspective. The EF Core backend is untouched.

The core task is implementing two `ExecuteInLeaseAsync` overloads using the `SemaphoreSlim` pattern that
already exists in `InMemoryLeasingScopeFactory` — that class is being made redundant by this phase. The
implementation moves its lock-per-queue logic inside `InMemoryStorage` as an instance field, eliminating the
static `ConcurrentDictionary` test-isolation bug that has been tracked as deferred tech debt.

The secondary task is hardening `_queues` inner collection thread-safety by replacing `HashSet<Guid>` with
`ConcurrentDictionary<Guid, byte>`, and making `UpsertScheduleAsync` atomic via a dedicated `_scheduleLock`.
Three existing test assertions that reflect on `_queues` by field type will need type updates — this is a
mechanical change, not a logical one.

**Primary recommendation:** Implement `ExecuteInLeaseAsync<TResult>` first, delegate the non-generic overload
to it (D-03), then address collection type changes, then schedule locking, then tests. That ordering means
each step is independently verifiable.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Per-queue mutual exclusion | InMemoryStorage (instance) | — | Lock must be instance-scoped to allow test isolation; `_semaphores` dict is the owner |
| Callback execution inside lease | InMemoryStorage.ExecuteInLeaseAsync | QueuePoller / SchedulePoller (callers) | Backend owns the lock lifecycle; callers only supply the callback |
| Schedule write atomicity | InMemoryStorage._scheduleLock | — | `_schedules` is a non-concurrent Dictionary; all writes need a single guard |
| Queue index thread-safety | ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>> | — | InsertAsync runs without a lock; inner collection must be intrinsically safe |
| Test file deletion | Test project (filesystem) | — | `InMemoryLeasingScopeFactoryTests.cs` references a class deleted in Phase 1 |

---

## Current Codebase State

### Phase 1 Completion Verified

The following changes from Phase 1 are confirmed present in the working tree:

- `IAtomizerStorage` — has both `ExecuteInLeaseAsync<TResult>` and `ExecuteInLeaseAsync` non-generic
  overloads with full XML documentation. [VERIFIED: read file]
- `InMemoryStorage.ExecuteInLeaseAsync` — both overloads are stubbed with `throw new NotImplementedException()`.
  [VERIFIED: read file, lines 221-239]
- `EntityFrameworkCoreStorage.ExecuteInLeaseAsync` — both overloads are stubbed with
  `throw new NotImplementedException()`. [VERIFIED: grep result]
- `QueuePoller.RunAsync` — calls `storage.ExecuteInLeaseAsync(queue.QueueKey, async innerCt => { ... }, ct)`
  with the generic overload, receiving `List<AtomizerJob>`. [VERIFIED: read file]
- `SchedulePoller.RunAsync` — calls `storage.ExecuteInLeaseAsync(QueueKey.Scheduler, async innerCt => { ... }, ioToken)`
  with the non-generic overload. [VERIFIED: read file]
- `IAtomizerServiceScope` — exposes only `IAtomizerStorage Storage { get; }` — `LeasingScopeFactory` is
  gone. [VERIFIED: read file]
- DI registrations — no `IAtomizerLeasingScopeFactory` or `NoopLeasingScopeFactory` in
  `ServiceCollectionExtensions`. [VERIFIED: grep result]

**Note:** `InMemoryLeasingScopeFactory.cs` still exists in `src/Atomizer/Storage/` but it is no longer
registered or called anywhere in the main code path. It is a dead file. Phase 2 may leave it in place (it
will become truly unreachable after `InMemoryStorage.ExecuteInLeaseAsync` is implemented) or delete it as
cleanup — the planner can decide; nothing breaks either way. [VERIFIED: grep of src/]

### Build State

Build succeeds with 0 errors. 74 tests pass on net8.0 and net10.0. net6.0 target fails due to missing
runtime on this machine (not a code defect). [VERIFIED: `dotnet build` and `dotnet test` output]

---

## Standard Stack

### Core (all already present — no new dependencies)

| Library | Version | Purpose | Already Used |
|---------|---------|---------|--------------|
| `System.Collections.Concurrent` | BCL | `ConcurrentDictionary<K,V>` for `_jobs`, `_leasesByToken`, new `_queues` and `_semaphores` | Yes |
| `System.Threading.SemaphoreSlim` | BCL | Per-queue mutex with async `WaitAsync(TimeSpan, CancellationToken)` | Yes (in `InMemoryLeasingScopeFactory`) |
| `Microsoft.Extensions.Logging.Abstractions` | 6.0+ | `ILogger<T>` for skip-tick log message | Yes |

[VERIFIED: InMemoryStorage.cs and InMemoryLeasingScopeFactory.cs imports — no new packages needed]

**Installation:** None — all dependencies are BCL or already referenced.

---

## Architecture Patterns

### System Architecture Diagram

```
AtomizerClient.ScheduleRecurringAsync
        │
        ▼
InMemoryStorage.UpsertScheduleAsync
        │  await _scheduleLock.WaitAsync(ct)    ← new in Phase 2
        │  _schedules[key] = schedule           ← plain Dictionary, safe under lock
        │  _scheduleLock.Release()
        ▼
    returns Guid


QueuePoller / SchedulePoller
        │
        ▼
InMemoryStorage.ExecuteInLeaseAsync<TResult>(queue, callback, ct)   ← new in Phase 2
        │
        ├─ semaphore = _semaphores.GetOrAdd(queue, _ => new SemaphoreSlim(1,1))
        │
        ├─ acquired = await semaphore.WaitAsync(TimeSpan.Zero, ct)
        │         │
        │    false ┤── return default(TResult)   ← skip this tick, no callback
        │         │
        │    true  ┤── try
        │              │   return await callback(ct)    ← GetDueJobsAsync + UpdateJobsAsync inside
        │              │
        │              finally
        │                  semaphore.Release()
        ▼
    TResult (list of leased jobs, or void)
```

### Recommended Project Structure

No structural changes to the project layout. All work touches existing files plus one new test file:

```
src/Atomizer/Storage/
├── InMemoryStorage.cs        ← primary edit target
├── InMemoryLeasingScopeFactory.cs   ← dead file; leave or delete
└── InMemoryJobStorageOptions.cs     ← unchanged

tests/Atomizer.Tests/Storage/
├── InMemoryStorageTests.cs          ← update _queues field-type assertions only
├── InMemoryStorageLeaseTests.cs     ← NEW: lease-specific tests
└── InMemoryLeasingScopeFactoryTests.cs  ← DELETE
```

### Pattern 1: ExecuteInLeaseAsync — Zero-timeout semaphore acquire with callback

**What:** Acquire a per-queue `SemaphoreSlim(1,1)` with `TimeSpan.Zero` timeout. If not acquired, skip.
If acquired, run the callback inside a `try/finally` that always releases.

**When to use:** Every `ExecuteInLeaseAsync` call from `QueuePoller` and `SchedulePoller`.

```csharp
// Source: CONTEXT.md D-02 / D-04 canonical pattern, verified against InMemoryLeasingScopeFactory
public Task<TResult> ExecuteInLeaseAsync<TResult>(
    QueueKey queue,
    Func<CancellationToken, Task<TResult>> callback,
    CancellationToken cancellationToken
)
{
    var semaphore = _semaphores.GetOrAdd(queue, _ => new SemaphoreSlim(1, 1));
    var acquired = await semaphore.WaitAsync(TimeSpan.Zero, cancellationToken);
    if (!acquired)
    {
        _logger.LogDebug("ExecuteInLeaseAsync: skipping tick for queue {QueueKey} — lease already held", queue);
        return default!;
    }
    try
    {
        return await callback(cancellationToken);
    }
    finally
    {
        semaphore.Release();
    }
}
```

**Note on `default!`:** The generic overload returns `default(TResult)` on miss. For `TResult = List<AtomizerJob>`,
`default` is `null`. The caller (`QueuePoller`) already guards against null/empty: `leasedJobs = await ...`
followed by `if (leasedJobs.Count > 0)`. However, `null?.Count` would throw. Research confirms the caller
does `if (leasedJobs.Count > 0)` without a null check — so the implementation should return an empty
collection rather than `null` for list-typed results, OR the caller must be verified to handle null. See
Pitfall 2 below.

### Pattern 2: Non-generic overload delegation (D-03)

**What:** Delegate to the generic overload to avoid duplicating the lock path.

```csharp
// Source: CONTEXT.md D-03 specifics section
public Task ExecuteInLeaseAsync(
    QueueKey queue,
    Func<CancellationToken, Task> callback,
    CancellationToken cancellationToken
) => ExecuteInLeaseAsync<bool>(queue, async ct => { await callback(ct); return true; }, cancellationToken);
```

### Pattern 3: Schedule lock — always-wait acquire

**What:** `_scheduleLock` (a single `SemaphoreSlim(1,1)` instance field) guards all reads and writes to
`_schedules`. Unlike the queue semaphore, this always waits (`WaitAsync(cancellationToken)`) because
`UpsertScheduleAsync` is a client write call, not a skip-on-busy poll.

```csharp
// Source: CONTEXT.md D-05 / D-06
public async Task<Guid> UpsertScheduleAsync(AtomizerSchedule schedule, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    await _scheduleLock.WaitAsync(cancellationToken);
    try
    {
        var now = _clock.UtcNow;
        schedule.CreatedAt = schedule.CreatedAt == default ? now : schedule.CreatedAt;
        schedule.UpdatedAt = now;
        _schedules[schedule.JobKey] = schedule;
        _logger.LogDebug("UpsertSchedule: upserted schedule for jobKey={JobKey}", schedule.JobKey);
        return schedule.Id;
    }
    finally
    {
        _scheduleLock.Release();
    }
}
```

### Pattern 4: ConcurrentDictionary<Guid, byte> as a concurrent HashSet

**What:** .NET has no `ConcurrentHashSet<T>`. The canonical substitute is
`ConcurrentDictionary<T, byte>` where the value is always `0`. [VERIFIED: BCL knowledge; no library needed]

```csharp
// Replace:
private readonly Dictionary<QueueKey, HashSet<Guid>> _queues = new();

// With:
private readonly ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>> _queues = new();

// IndexIntoQueue helper becomes:
private void IndexIntoQueue(AtomizerJob job)
{
    var ids = _queues.GetOrAdd(job.QueueKey, _ => new ConcurrentDictionary<Guid, byte>());
    ids[job.Id] = 0;
}

// UnindexFromQueue helper becomes:
private void UnindexFromQueue(AtomizerJob job)
{
    if (_queues.TryGetValue(job.QueueKey, out var ids))
    {
        ids.TryRemove(job.Id, out _);
        // Note: do NOT remove empty inner dict — race between remove-check and concurrent add
    }
}
```

**Note on empty-queue removal:** The current `UnindexFromQueue` removes the outer key when the inner set
becomes empty. With `ConcurrentDictionary` this check-then-remove is a non-atomic TOCTOU — removing the
key while another thread is adding a job to that queue causes `GetOrAdd` to re-create the inner dict and
lose visibility. The safe approach is to **not** remove the empty inner dict from the outer dictionary.
The existing `GetDueJobsAsync` already handles the empty-inner-dict case via `ids.Count == 0` check.
This is a behavioral nuance the planner must address.

### Anti-Patterns to Avoid

- **Static semaphore dictionary:** The old `InMemoryLeasingScopeFactory.Semaphores` was `static`, causing
  state to bleed between test instances. The new `_semaphores` is an **instance field** — do not make it
  static.
- **Holding the semaphore across `InsertAsync`:** `InsertAsync` runs without holding any per-queue lock.
  That is intentional — inserting a new job should not contend with the poll path. Only `GetDueJobsAsync`
  + `UpdateJobsAsync` need to run under the semaphore (via the callback).
- **Calling `semaphore.Release()` when not acquired:** The `finally` block in `ExecuteInLeaseAsync` should
  only release if `acquired == true`. If the early-return path is taken before `try`, `Release()` is never
  reached — which is correct.
- **Forgetting async on the generic overload:** The method body has `await semaphore.WaitAsync(...)` and
  `await callback(...)`, so it must be `public async Task<TResult>`, not returning `Task.FromResult`.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Concurrent hash set | Custom `SynchronizedHashSet<T>` | `ConcurrentDictionary<T, byte>` | BCL-idiomatic; zero overhead; no lock needed |
| Per-queue mutual exclusion | Custom `Monitor`-based lock | `SemaphoreSlim(1,1)` with `WaitAsync` | Supports async callers; established pattern in codebase |
| Async-safe schedule read-write guard | `lock(obj)` statement | `SemaphoreSlim(1,1)` with `WaitAsync(ct)` | `lock` cannot be used across `await`; `SemaphoreSlim` is the correct substitute |

**Key insight:** `lock` statements are incompatible with `await` in C#. Every guard that protects async
operations must use `SemaphoreSlim`, `Monitor.TryEnter`/`Exit` with synchronous code only, or an
`AsyncLock`-style wrapper. This codebase uses `SemaphoreSlim` — stay consistent.

---

## Runtime State Inventory

This is a code-only refactor of the InMemory backend. There is no persistent state to migrate.

| Category | Items Found | Action Required |
|----------|-------------|-----------------|
| Stored data | None — InMemoryStorage is purely in-process | None |
| Live service config | None — no external services involved | None |
| OS-registered state | None | None |
| Secrets/env vars | None | None |
| Build artifacts | None relevant | None |

---

## Common Pitfalls

### Pitfall 1: `default!` returns null for reference-type TResult

**What goes wrong:** `ExecuteInLeaseAsync<List<AtomizerJob>>` returns `default!` on miss, which is `null`.
`QueuePoller` does `leasedJobs = await storage.ExecuteInLeaseAsync(...)` and then
`if (leasedJobs.Count > 0)` — this throws `NullReferenceException` if `leasedJobs` is null.

**Why it happens:** `default(List<T>)` is null for reference types.

**How to avoid:** Return an empty `List<AtomizerJob>()` when the lease is not acquired, not `default!`.
Alternatively, the generic overload cannot know the correct empty value for `TResult`, so the implementation
could use `default!` for the generic overload and have the non-generic overload return `Task.CompletedTask`
directly. However, the cleanest fix is to have `QueuePoller` null-check its result, OR to document that
`ExecuteInLeaseAsync` returns `default!` and callers must handle it. The CONTEXT.md says "returns
`default(TResult)`" — this is the specified contract. The planner should verify `QueuePoller.RunAsync`
null-checks the result.

**Verification:** Read `QueuePoller.RunAsync` lines 57-90 — `leasedJobs = await ...` then
`if (leasedJobs.Count > 0)` with no null guard. This is a real gap. The plan must either:
(a) return `new List<AtomizerJob>()` from the non-acquired path by casting, or
(b) add a null-coalescing guard in `QueuePoller`.

Since the CONTEXT.md says "returns `default(TResult)`", option (b) is the safer approach to avoid a
behavioral divergence from the stated contract. Option (a) is also acceptable and avoids changing a
file outside the phase boundary. The planner should pick one and document the choice.

**Warning signs:** `NullReferenceException` in `QueuePoller.RunAsync` during integration test execution
after Phase 2 lands.

### Pitfall 2: `_semaphores` lookup for `QueueKey.Scheduler`

**What goes wrong:** If `ExecuteInLeaseAsync` uses `_semaphores.GetOrAdd(queue, ...)` uniformly and D-05
says `_scheduleLock` guards `_schedules`, there are two possible designs:

- **Design A (unified):** `_scheduleLock` IS the semaphore stored under `QueueKey.Scheduler` in
  `_semaphores`. `_scheduleLock` field is a getter alias: `private SemaphoreSlim _scheduleLock => _semaphores.GetOrAdd(QueueKey.Scheduler, _ => new SemaphoreSlim(1,1))`. A single code path handles all queues.
- **Design B (separate):** `_scheduleLock` is a dedicated field; `UpsertScheduleAsync` uses it; but
  `ExecuteInLeaseAsync` for `QueueKey.Scheduler` uses `_semaphores` (a different semaphore). `UpsertScheduleAsync`
  and `ExecuteInLeaseAsync(QueueKey.Scheduler, ...)` would use **different** locks — no atomicity
  guarantee between them.

**The bug:** Design B silently fails to provide the guarantee INMEM-03 requires. If `UpsertScheduleAsync`
holds `_scheduleLock` while the scheduler lease holds a different semaphore, they are NOT mutually exclusive.

**How to avoid:** The CONTEXT.md says D-05 is "planner's choice" between the two designs, but the research
shows Design A (unified, single semaphore per queue key including `QueueKey.Scheduler`) is the only design
that satisfies INMEM-03 when `UpsertScheduleAsync` and `ExecuteInLeaseAsync(QueueKey.Scheduler, ...)` must
be mutually exclusive. The planner should choose Design A.

**Warning signs:** Concurrent `UpsertScheduleAsync` and `GetDueSchedulesAsync` calls see inconsistent
`_schedules` state.

### Pitfall 3: `UnindexFromQueue` TOCTOU on empty-dict removal

**What goes wrong:** After changing `_queues` to `ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>>`,
the existing pattern of removing the outer key when the inner dict becomes empty is a race:

```
Thread A: ids.TryRemove(job.Id) → ids.Count == 0 → _queues.TryRemove(key)
Thread B: _queues.GetOrAdd(key, ...) → new inner dict           ← A's remove wins, B sees empty outer
Thread B: ids[newJob.Id] = 0         ← on the OLD inner dict that was just removed, invisible to _queues
```

**How to avoid:** Stop removing the empty outer key. An empty `ConcurrentDictionary<Guid, byte>` is
harmless and `GetDueJobsAsync` already handles empty: `if (!_queues.TryGetValue(queueKey, out var ids) || ids.Count == 0)`.

### Pitfall 4: Test field-type assertions will fail after `_queues` type change

**What goes wrong:** `InMemoryStorageTests.InsertAsync_WhenCalled_ShouldStoreJobAndIndexQueue` uses:

```csharp
var queues = NonPublicSpy.GetFieldValue<InMemoryStorage, Dictionary<QueueKey, HashSet<Guid>>>("_queues", _sut);
queues[QueueKey.Default].Should().Contain(job.Id);
```

After the type change, `NonPublicSpy.GetFieldValue<..., Dictionary<QueueKey, HashSet<Guid>>>` will throw
a cast exception because the field is now `ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>>`.

**How to avoid:** Update the test assertion to:

```csharp
var queues = NonPublicSpy.GetFieldValue<InMemoryStorage, ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>>>("_queues", _sut);
queues[QueueKey.Default].Should().ContainKey(job.Id);
```

Similarly update `GetDueSchedulesAsync_WhenDueSchedulesExist_ShouldGetSchedules` which reads `_schedules`
by type (field type is unchanged — `Dictionary<JobKey, AtomizerSchedule>` — but check `UpsertScheduleAsync`
test also reads it). [VERIFIED: InMemoryStorageTests.cs lines 43-51, 215-219, 246-252]

### Pitfall 5: Forgetting `async` on `ExecuteInLeaseAsync<TResult>`

**What goes wrong:** The method needs `await semaphore.WaitAsync(...)` and `await callback(...)`. If the
method is not declared `async`, these awaits either cause compile errors or require manual `Task` chaining.

**How to avoid:** Declare `public async Task<TResult> ExecuteInLeaseAsync<TResult>(...)`.

---

## Code Examples

### ExecuteInLeaseAsync — full implementation

```csharp
// Source: CONTEXT.md Specifics section + verified SemaphoreSlim pattern from InMemoryLeasingScopeFactory.cs
public async Task<TResult> ExecuteInLeaseAsync<TResult>(
    QueueKey queue,
    Func<CancellationToken, Task<TResult>> callback,
    CancellationToken cancellationToken
)
{
    var semaphore = _semaphores.GetOrAdd(queue, _ => new SemaphoreSlim(1, 1));
    var acquired = await semaphore.WaitAsync(TimeSpan.Zero, cancellationToken);
    if (!acquired)
    {
        _logger.LogDebug("ExecuteInLeaseAsync: skipping tick for queue {QueueKey} — lease already held", queue);
        return default!;
    }
    try
    {
        return await callback(cancellationToken);
    }
    finally
    {
        semaphore.Release();
    }
}

public Task ExecuteInLeaseAsync(
    QueueKey queue,
    Func<CancellationToken, Task> callback,
    CancellationToken cancellationToken
) => ExecuteInLeaseAsync<bool>(queue, async ct => { await callback(ct); return true; }, cancellationToken);
```

### SemaphoreSlim.WaitAsync — zero-timeout signature

```csharp
// Source: BCL documentation — three WaitAsync overloads relevant here:
// bool WaitAsync(TimeSpan timeout)           — synchronous timeout, no CT
// Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct)  ← this one
// Task WaitAsync(CancellationToken ct)        — always waits, no timeout

// For zero-timeout skip-on-busy:
var acquired = await semaphore.WaitAsync(TimeSpan.Zero, cancellationToken);

// For always-wait (schedule lock):
await _scheduleLock.WaitAsync(cancellationToken);
```

[VERIFIED: BCL `SemaphoreSlim` — both overloads present since .NET Framework 4.5 / .NET Standard 2.0]

### ConcurrentDictionary<Guid, byte> as concurrent set

```csharp
// Source: CONTEXT.md D-07 and established .NET idiom [ASSUMED: training knowledge — no external lookup needed]
// Add:
ids.TryAdd(job.Id, 0);   // or ids[job.Id] = 0;

// Contains:
ids.ContainsKey(job.Id);

// Remove:
ids.TryRemove(job.Id, out _);

// Count:
ids.Count
```

### New field declarations for InMemoryStorage

```csharp
// Replace:
private readonly Dictionary<QueueKey, HashSet<Guid>> _queues = new();

// With:
private readonly ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>> _queues = new();
private readonly ConcurrentDictionary<QueueKey, SemaphoreSlim> _semaphores = new();

// Add (for unified schedule lock — Design A):
// _scheduleLock is the semaphore stored under QueueKey.Scheduler in _semaphores.
// No separate field needed if Design A is chosen.
// If Design B is chosen (separate field), add:
// private readonly SemaphoreSlim _scheduleLock = new(1, 1);
```

---

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|------------------|--------|
| `IAtomizerLeasingScopeFactory` pattern (scope object, `Acquired` flag) | Callback-based `ExecuteInLeaseAsync` (lock lifecycle inside storage) | Callers simpler; no `if (!scope.Acquired) return` boilerplate |
| Static `Semaphores` dict (process-wide, test-bleed) | Instance-field `_semaphores` dict per `InMemoryStorage` instance | Full test isolation — each `new InMemoryStorage(...)` has its own locks |
| `Dictionary<QueueKey, HashSet<Guid>> _queues` (guarded comment only) | `ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>>` | Real thread safety without external lock on insert path |

**Deprecated/outdated (Phase 2):**
- `InMemoryLeasingScopeFactory`: dead file after Phase 2. No callers remain. Can be deleted as cleanup.
- `InMemoryLeasingScopeFactoryTests.cs`: references `InMemoryLeasingScopeFactory`; must be deleted (D-10).

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `SemaphoreSlim.WaitAsync(TimeSpan.Zero, CancellationToken)` is available on `netstandard2.0` | Standard Stack / Code Examples | Low — this overload has been available since .NET Framework 4.5; would require fallback only on very old runtimes not targeted here |
| A2 | `QueuePoller` accesses `leasedJobs.Count` without null guard | Pitfall 1 | Medium — if wrong, no NullReferenceException risk; but plan step for null-coalescing guard is unnecessary work |

**Verification for A1:** [ASSUMED] — `SemaphoreSlim` has been `netstandard2.0`-compatible since its introduction.
The project targets `netstandard2.0;net8.0;net10.0` and existing code in `InMemoryLeasingScopeFactory`
already calls `semaphore.WaitAsync(TimeSpan.Zero, cancellationToken)` — confirmed compatible. [VERIFIED: InMemoryLeasingScopeFactory.cs line 62]

**Verification for A2:** [VERIFIED: QueuePoller.cs lines 57-89] — `leasedJobs = await storage.ExecuteInLeaseAsync(...)` then `if (leasedJobs.Count > 0)`. No null check present.

---

## Open Questions

1. **Null return vs. empty list from non-acquired path**
   - What we know: CONTEXT.md D-02 says "returns `default(TResult)`". `QueuePoller` does `leasedJobs.Count`
     immediately after without null guard.
   - What's unclear: Did the author intend the caller to null-check, or was it assumed TResult would always
     be a value type / non-null default?
   - Recommendation: Add `leasedJobs ??= new List<AtomizerJob>()` null-coalescing assignment in
     `QueuePoller.RunAsync` (one-line fix, keeps the `default!` contract intact). This is Phase 2 scope
     since it's a direct consequence of implementing `ExecuteInLeaseAsync`.

2. **`InMemoryLeasingScopeFactory.cs` — delete or leave?**
   - What we know: The file is dead code after Phase 2. It is `internal sealed class` so it cannot leak.
   - What's unclear: Is deleting it inside Phase 2 scope?
   - Recommendation: Include deletion in Phase 2 (same PR as the `InMemoryLeasingScopeFactoryTests.cs`
     deletion). Both are dead artifacts of the same removed abstraction.

---

## Environment Availability

Step 2.6: Skipped — this phase is purely in-process code changes. No external tools, databases, CLIs, or
services are involved.

**Runtime note:** .NET 6.0 runtime is absent on this machine; net6.0 target tests cannot run locally.
net8.0 and net10.0 tests run cleanly. This is a pre-existing environment condition, not a Phase 2 issue.
[VERIFIED: test run output]

---

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit v3 (2.0.1) + AwesomeAssertions 9.1.0 + NSubstitute 5.3.0 |
| Config file | `tests/Atomizer.Tests/Atomizer.Tests.csproj` |
| Quick run command | `dotnet test tests/Atomizer.Tests/Atomizer.Tests.csproj --no-build -l "console;verbosity=minimal"` |
| Full suite command | `dotnet test --no-build -l "console;verbosity=minimal"` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| INMEM-01 | ExecuteInLeaseAsync satisfies interface contract — callers see same behavior as EF Core | unit | `dotnet test tests/Atomizer.Tests/ --filter "FullyQualifiedName~InMemoryStorageLeaseTests" --no-build` | ❌ Wave 0 |
| INMEM-02 | Semaphore held for full callback duration; concurrent callers serialized; exception releases semaphore | unit | `dotnet test tests/Atomizer.Tests/ --filter "FullyQualifiedName~InMemoryStorageLeaseTests" --no-build` | ❌ Wave 0 |
| INMEM-03 | UpsertScheduleAsync is atomic under concurrent calls | unit | `dotnet test tests/Atomizer.Tests/ --filter "FullyQualifiedName~InMemoryStorageLeaseTests" --no-build` | ❌ Wave 0 |
| (existing) | All 74 existing tests continue to pass | unit | `dotnet test tests/Atomizer.Tests/ --no-build -l "console;verbosity=minimal"` | ✅ |

### Sampling Rate

- **Per task commit:** `dotnet test tests/Atomizer.Tests/ --no-build -l "console;verbosity=minimal"`
- **Per wave merge:** Same — full unit test suite
- **Phase gate:** Full suite green on net8.0 and net10.0 before `/gsd-verify-work`

### Wave 0 Gaps

- [ ] `tests/Atomizer.Tests/Storage/InMemoryStorageLeaseTests.cs` — covers INMEM-01, INMEM-02, INMEM-03
  - Test cases required (from CONTEXT.md D-09):
    - Semaphore held for full callback duration (assert semaphore count before/after)
    - Concurrent callers serialized (two concurrent `ExecuteInLeaseAsync` calls on same queue — second
      should get `default` or skip)
    - Exception releases semaphore (throw inside callback — verify semaphore is released, next call acquires)
    - Non-acquired case returns empty / default (semaphore already held, second caller gets `default!`)
    - `QueueKey.Scheduler` uses the same lock as `UpsertScheduleAsync` (concurrent calls mutually exclusive)

---

## Security Domain

This phase contains no authentication, session management, access control, cryptography, or externally
supplied input handling. It is a pure in-process concurrency implementation within a trusted library. ASVS
categories V2–V6 do not apply.

---

## Sources

### Primary (HIGH confidence)
- `src/Atomizer/Storage/InMemoryStorage.cs` — read directly; current stub state verified
- `src/Atomizer/Abstractions/IAtomizerStorage.cs` — read directly; Phase 1 interface shape verified
- `src/Atomizer/Processing/QueuePoller.cs` — read directly; call site shape verified
- `src/Atomizer/Scheduling/SchedulePoller.cs` — read directly; call site shape verified
- `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs` — read directly; semaphore pattern extracted
- `tests/Atomizer.Tests/Storage/InMemoryStorageTests.cs` — read directly; field-type assertions identified
- `tests/Atomizer.Tests/Storage/InMemoryLeasingScopeFactoryTests.cs` — read directly; confirmed deletable
- `.planning/phases/02-inmemory-implementation/02-CONTEXT.md` — read directly; all decisions locked
- `.planning/phases/01-leasing-abstraction/01-CONTEXT.md` — read directly; upstream decisions confirmed
- `dotnet build` + `dotnet test` output — verified 0 errors, 74 passing tests

### Secondary (MEDIUM confidence)
- BCL `SemaphoreSlim` documentation patterns — confirmed consistent with existing codebase usage

### Tertiary (LOW confidence)
- None

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all dependencies are BCL; no new packages; verified against codebase
- Architecture: HIGH — interface shape finalized in Phase 1; implementation pattern extracted directly from
  `InMemoryLeasingScopeFactory` which is the model to replicate
- Pitfalls: HIGH — pitfalls 1 and 4 are verified by reading actual source files; pitfalls 2, 3, 5 are
  derived from C# concurrency fundamentals confirmed against the codebase

**Research date:** 2026-05-03
**Valid until:** Stable — no external dependencies; valid as long as the codebase matches the read state
