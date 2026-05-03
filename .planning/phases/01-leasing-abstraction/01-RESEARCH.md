# Phase 1: Leasing Abstraction - Research

**Researched:** 2026-05-03
**Domain:** C# interface design, ASP.NET Core background services, leasing/locking abstraction
**Confidence:** HIGH — all findings verified by direct codebase inspection

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- **D-01:** `ExecuteInLeaseAsync` is **generic** — signature: `Task<TResult> ExecuteInLeaseAsync<TResult>(QueueKey queue, Func<CancellationToken, Task<TResult>> callback, CancellationToken cancellationToken)`. Callers receive the return value directly; no closure/shared mutable state needed.
- **D-02:** A **non-generic overload** also exists: `Task ExecuteInLeaseAsync(QueueKey queue, Func<CancellationToken, Task> callback, CancellationToken cancellationToken)`. `SchedulePoller` and any caller with no return value uses this.
- **D-03:** `GetDueJobsAsync` / `GetDueSchedulesAsync` are **called by the caller from inside the callback** — storage only manages the lease scope, not what happens inside it. The contract stays open and backend-agnostic.
- **D-04:** If the callback throws, `ExecuteInLeaseAsync` **rolls back / releases the lease** (EF Core rolls back transaction; InMemory releases semaphore) and **rethrows** the exception unchanged to the caller.
- **D-05:** Both `InMemoryStorage.ExecuteInLeaseAsync` and `EntityFrameworkCoreStorage.ExecuteInLeaseAsync` throw `NotImplementedException` in Phase 1, annotated with:
  - `// TODO: Implemented in Phase 2`
  - `// TODO: Implemented in Phase 4`
- **D-06:** `QueuePoller` and `SchedulePoller` are **updated to the new call site in Phase 1**. The stubs throw, so existing integration tests that exercise the poll path will fail until Phase 2/4 — this is intentional and expected.
- **D-07:** `ReleaseLeasedAsync(LeaseToken, DateTimeOffset, CancellationToken)` **survives unchanged on `IAtomizerStorage`** in Phase 1.
- **D-08:** `QueuePump` continues to hold its own `LeaseToken` per-pump. No change to the shutdown path.
- **D-09:** `IAtomizerLeasingScopeFactory`, `IAtomizerLeasingScope`, and `NoopLeasingScopeFactory` are **all deleted in Phase 1**.
- **D-10:** `IAtomizerServiceScope.LeasingScopeFactory` property is **removed**. The service scope only needs to expose `Storage`.

### Claude's Discretion

- Exact XML documentation wording on the new interface members.
- Whether both overloads live on the same interface or one delegates to the other internally.

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope.

</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| LEASE-01 | Storage consumers call a single `ExecuteInLeaseAsync(queue, callback)` method — the open transaction/lock is managed inside the callback, not by the caller | New method added to `IAtomizerStorage`; `QueuePoller`/`SchedulePoller` rewritten to use it |
| LEASE-02 | `IAtomizerLeasingScopeFactory` and `IAtomizerLeasingScope` are removed from the public API; `Acquired` flag is gone | 4 source files deleted; 1 property removed from `IAtomizerServiceScope`; DI registration removed |
| LEASE-03 | The leasing contract does not assume SQL transactions — each backend decides how to implement atomicity | Interface signature uses only `QueueKey` + `Func` + `CancellationToken`; no SQL primitives |
| COMPAT-01 | `IAtomizerStorage` interface is updated to reflect the new leasing contract — this is a documented breaking change | `ExecuteInLeaseAsync` added; existing members unchanged (except removal of separate leasing path) |

</phase_requirements>

---

## Summary

Phase 1 is a pure interface and call-site refactor. No business logic changes. The work involves adding two method signatures to `IAtomizerStorage`, deleting four files that implement the old leasing abstraction, removing one property from `IAtomizerServiceScope`, updating two callers (`QueuePoller`, `SchedulePoller`) to the new call site, stubbing both storage implementations with `NotImplementedException`, and removing two DI registration lines plus the `LeasingScopeOptions` field from `AtomizerOptions`.

The codebase builds cleanly today (0 errors, 38 vulnerability warnings that are suppressed by `NU1901-NU1904` exclusions). All changes are confined to the `src/Atomizer` project and `src/Atomizer.EntityFrameworkCore` project — no changes to tests, samples, or build configuration are required in Phase 1 (test changes are noted as intentionally broken until Phase 2/4).

**Primary recommendation:** Implement in a single logical wave: interface first, then callers, then stubs, then DI cleanup, then deletions — all in one coherent changeset that keeps the build passing at each step.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Lease contract definition | Abstractions (`IAtomizerStorage`) | — | Storage owns its own locking strategy per D-03 |
| Caller call site (queue polling) | Processing (`QueuePoller`) | — | Caller moves work inside callback; no tier change |
| Caller call site (schedule polling) | Scheduling (`SchedulePoller`) | — | Same pattern as QueuePoller |
| Stub implementation (InMemory) | Storage (`InMemoryStorage`) | — | NotImplementedException stubs Phase 2 work |
| Stub implementation (EF Core) | Storage (`EntityFrameworkCoreStorage`) | — | NotImplementedException stubs Phase 4 work |
| DI wiring | Configuration (`ServiceCollectionExtensions`, `AtomizerOptions`) | — | Registration of deleted factory removed here |
| Deleted leasing types | Abstractions / Core | EF Core Storage | Files deleted; no replacement in Phase 1 |

---

## Standard Stack

### Core (no new dependencies — this is a refactor)

| Library | Version | Purpose | Notes |
|---------|---------|---------|-------|
| `System.Threading.Tasks` (BCL) | netstandard2.0+ | `Task` / `Task<TResult>` return types on new interface members | Already in use throughout |
| `Microsoft.Extensions.DependencyInjection` | `[6.0.0, )` | DI registration cleanup | Already a dependency |

No new NuGet packages are required for Phase 1. [VERIFIED: codebase inspection]

---

## Architecture Patterns

### System Architecture — Before vs After Phase 1

**Before (current):**
```
QueuePoller
  ├── scope.LeasingScopeFactory.CreateScopeAsync(queue, timeout, ct)
  │     └── IAtomizerLeasingScope { Acquired }
  ├── if (leasingScope.Acquired) { storage.GetDueJobsAsync(...) }
  └── leasingScope.Dispose() / DisposeAsync()

SchedulePoller
  ├── scope.LeasingScopeFactory.CreateScopeAsync(QueueKey.Scheduler, ...)
  ├── if (leasingScope.Acquired) { storage.GetDueSchedulesAsync(...) }
  └── leasingScope.Dispose() / DisposeAsync()
```

**After Phase 1:**
```
QueuePoller
  └── scope.Storage.ExecuteInLeaseAsync(queue, async ct => {
          jobs = await storage.GetDueJobsAsync(...)
          ... lease + update ...
          return jobs
      }, ct)

SchedulePoller
  └── scope.Storage.ExecuteInLeaseAsync(QueueKey.Scheduler, async ct => {
          schedules = await storage.GetDueSchedulesAsync(...)
          ... process ...
      }, ct)
```

### Recommended File Structure Changes

```
src/Atomizer/
├── Abstractions/
│   ├── IAtomizerStorage.cs        ← ADD ExecuteInLeaseAsync<TResult> + ExecuteInLeaseAsync overloads
│   ├── IAtomizerServiceScope.cs   ← REMOVE LeasingScopeFactory property
│   ├── IAtomizerLeasingScope.cs   ← DELETE
│   └── IAtomizerLeasingScopeFactory.cs  ← DELETE
├── Core/
│   ├── ServiceProviderServiceScope.cs  ← REMOVE LeasingScopeFactory resolution
│   └── NoopLeasingScopeFactory.cs  ← DELETE
├── Configuration/
│   ├── AtomizerOptions.cs          ← REMOVE LeasingScopeOptions field + NoopLeasingScopeFactory default
│   ├── LeasingScopeOptions.cs      ← DELETE
│   ├── ServiceCollectionExtensions.cs  ← REMOVE IAtomizerLeasingScopeFactory DI registration block
│   └── AtomizerOptionsExtensions.cs    ← REMOVE LeasingScopeOptions assignment in UseInMemoryStorage
├── Processing/
│   └── QueuePoller.cs             ← REWRITE leasing block to ExecuteInLeaseAsync call
├── Scheduling/
│   └── SchedulePoller.cs          ← REWRITE leasing block to ExecuteInLeaseAsync call
└── Storage/
    ├── InMemoryStorage.cs          ← ADD ExecuteInLeaseAsync stubs (NotImplementedException)
    └── InMemoryLeasingScopeFactory.cs  ← SURVIVES for now (deleted in Phase 5)

src/Atomizer.EntityFrameworkCore/
├── Storage/
│   ├── EntityFrameworkCoreStorage.cs     ← ADD ExecuteInLeaseAsync stubs (NotImplementedException)
│   ├── DatabaseTransactionLeasingScope.cs     ← SURVIVES for now (deleted in Phase 5)
│   └── DatabaseTransactionLeasingScopeFactory.cs  ← SURVIVES for now (deleted in Phase 5)
└── Extensions/
    └── AtomizerOptionsExtensions.cs  ← REMOVE LeasingScopeOptions assignment in UseEntityFrameworkCoreStorage
```

**Note on InMemoryLeasingScopeFactory, DatabaseTransactionLeasingScope, DatabaseTransactionLeasingScopeFactory:** These are NOT deleted in Phase 1 per ROADMAP.md Phase 5 success criteria — they disappear in the cleanup phase. Only the files explicitly listed under "Types Being Deleted" in CONTEXT.md are removed now.

**CORRECTION — re-read CONTEXT.md and ROADMAP.md carefully:**

CONTEXT.md Canonical Refs says:
- `IAtomizerLeasingScopeFactory.cs` — Deleted in this phase
- `IAtomizerLeasingScope.cs` — Deleted in this phase
- `NoopLeasingScopeFactory.cs` — Deleted in this phase
- `IAtomizerServiceScope.cs` — Remove `LeasingScopeFactory` property (not deleted)

ROADMAP.md Phase 5 lists `InMemoryLeasingScopeFactory`, `DatabaseTransactionLeasingScopeFactory`, `DatabaseTransactionLeasingScope` for deletion in Phase 5.

So Phase 1 deletes exactly: `IAtomizerLeasingScope.cs`, `IAtomizerLeasingScopeFactory.cs`, `NoopLeasingScopeFactory.cs`, `LeasingScopeOptions.cs`.

`InMemoryLeasingScopeFactory.cs`, `DatabaseTransactionLeasingScope.cs`, `DatabaseTransactionLeasingScopeFactory.cs` survive until Phase 5.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Generic method on `netstandard2.0` interface | Guard with `#if` | Plain generic method — supported in `netstandard2.0` natively | Generic methods on interfaces are fully supported; only `IAsyncDisposable` and `await using` need `#if NETCOREAPP3_0_OR_GREATER` guards |
| `Task ExecuteInLeaseAsync` (non-generic) delegating to generic | Separate impl | Have the non-generic overload call the generic one with `Func<CancellationToken, Task<object?>>` wrapper, or implement independently | Claude's Discretion — either approach compiles; delegation is DRY |

**Key insight:** Generic methods on interfaces are fully compatible with `netstandard2.0`. No `#if` guards are needed for `ExecuteInLeaseAsync`. The existing `#if NETCOREAPP3_0_OR_GREATER` guards are only for `IAsyncDisposable` — not needed here.

---

## Common Pitfalls

### Pitfall 1: Leaving `scope.LeasingScopeFactory` access in `QueuePoller` / `SchedulePoller`
**What goes wrong:** The `IAtomizerServiceScope` interface no longer has `LeasingScopeFactory`; build fails with CS0117.
**Why it happens:** Both pollers currently access `scope.LeasingScopeFactory` on lines 57 and 52 respectively.
**How to avoid:** The pollers must be fully rewritten — the entire `leasingScopeFactory`, `leasingScope`, `Acquired` check, and `#if NETCOREAPP3_0_OR_GREATER` block is replaced by a single `ExecuteInLeaseAsync` call. The `leasedJobs` list initialization moves inside the callback.
**Warning signs:** Any reference to `leasingScopeFactory`, `leasingScope`, or `.Acquired` remaining in either file after the edit.

### Pitfall 2: Attempting to return data from the callback in `SchedulePoller` when using non-generic overload
**What goes wrong:** `SchedulePoller` processes schedules with side effects (no useful return value). If the generic overload is used it requires a return type — `Task<IReadOnlyList<AtomizerSchedule>>` works but the result is already acted upon inside the callback.
**Why it happens:** Confusion about which overload to use.
**How to avoid:** `SchedulePoller` uses the non-generic `Task ExecuteInLeaseAsync(QueueKey, Func<CancellationToken, Task>, CancellationToken)` overload (D-02). `QueuePoller` uses the generic overload returning `IReadOnlyList<AtomizerJob>` (or returns nothing from inside and uses a captured list — but D-01 says callers receive the return value).
**Warning signs:** `QueuePoller` callback has no return statement.

### Pitfall 3: Forgetting the `leasedJobs` channel-write logic is OUTSIDE the leasing scope in the current `QueuePoller`
**What goes wrong:** The current `QueuePoller.RunAsync` writes to the channel AFTER the `using var leasingScope` block ends (lines 111–135). With `ExecuteInLeaseAsync`, the callback only needs to fetch, lease, and update jobs — channel write still happens after the `await ExecuteInLeaseAsync(...)` call returns.
**Why it happens:** Structural misread of the current code.
**How to avoid:** The callback body should contain: `GetDueJobsAsync` → `job.Lease(...)` → `storage.UpdateJobsAsync(...)` → return jobs. The loop that does `channel.Writer.WriteAsync(job, ct)` stays outside the callback, iterating the returned list.
**Warning signs:** `WriteAsync` inside the `ExecuteInLeaseAsync` callback.

### Pitfall 4: `DegreeOfParallelism` skip check uses a captured variable that must survive refactor
**What goes wrong:** The current `QueuePoller` has `if (now - _lastStorageCheck >= storageCheckInterval && itemsInChannel < queue.DegreeOfParallelism)` wrapping the entire lease block. This outer `if` must remain — it guards whether to call `ExecuteInLeaseAsync` at all.
**Why it happens:** The `ExecuteInLeaseAsync` call site replaces the inner scope acquisition, not the outer timing check.
**How to avoid:** Keep the outer `if` intact. Only the inner `using var leasingScope ... if (leasingScope.Acquired) { ... }` block is replaced.

### Pitfall 5: `ServiceProviderServiceScope` still resolves `IAtomizerLeasingScopeFactory` from DI after the registration is removed
**What goes wrong:** `ServiceProviderServiceScope` constructor calls `scope.ServiceProvider.GetRequiredService<IAtomizerLeasingScopeFactory>()` — if the DI registration is removed but this line survives, runtime throws `InvalidOperationException`.
**Why it happens:** `ServiceProviderServiceScope.cs` must be updated in the same pass as `ServiceCollectionExtensions.cs`.
**How to avoid:** Remove the `LeasingScopeFactory` property from `IAtomizerServiceScope`, remove the `GetRequiredService<IAtomizerLeasingScopeFactory>()` call from `ServiceProviderServiceScope`, and remove the DI registration from `ServiceCollectionExtensions` — all in the same changeset.

### Pitfall 6: `AtomizerOptions.LeasingScopeOptions` default constructor references `NoopLeasingScopeFactory`
**What goes wrong:** After `NoopLeasingScopeFactory.cs` is deleted, `AtomizerOptions.cs` line 12 (`new LeasingScopeOptions(_ => new NoopLeasingScopeFactory())`) fails to compile.
**Why it happens:** `LeasingScopeOptions` and `NoopLeasingScopeFactory` are deleted in Phase 1; `AtomizerOptions` holds a field of type `LeasingScopeOptions`.
**How to avoid:** Remove the `LeasingScopeOptions` field from `AtomizerOptions` entirely in the same pass as deleting the `LeasingScopeOptions.cs` file.

### Pitfall 7: `AtomizerOptionsExtensions` (both `src/Atomizer` and `src/Atomizer.EntityFrameworkCore`) set `options.LeasingScopeOptions`
**What goes wrong:** After `LeasingScopeOptions` is removed from `AtomizerOptions`, both extension files still assign `options.LeasingScopeOptions = new LeasingScopeOptions(...)` — compile error.
**Why it happens:** Two extension files both reference the deleted `LeasingScopeOptions` property.
**How to avoid:** Remove the `options.LeasingScopeOptions = ...` lines from both `src/Atomizer/Configuration/AtomizerOptionsExtensions.cs` and `src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs`.

### Pitfall 8: `DatabaseTransactionLeasingScopeFactory` references `NoopLeasingScopeFactory`
**What goes wrong:** `DatabaseTransactionLeasingScopeFactory.cs` line 37 calls `new NoopLeasingScopeFactory()` as a fallback for non-relational providers. After `NoopLeasingScopeFactory` is deleted, this compile-fails.
**Why it happens:** The EF Core factory has a code path that delegates to `NoopLeasingScopeFactory` for non-relational databases (SQLite integration tests).
**How to avoid:** Since `DatabaseTransactionLeasingScopeFactory` itself survives until Phase 5, and `NoopLeasingScopeFactory` is deleted in Phase 1, the non-relational fallback code path in `DatabaseTransactionLeasingScopeFactory` must be updated. Options: inline the noop behavior directly, or (since the factory will be deleted in Phase 5 anyway) just remove the non-relational branch and throw `NotSupportedException` for non-relational providers in Phase 1. This is a minor decision for the planner.

### Pitfall 9: `ServiceCollectionExtensions` references `LeasingScopeOptions` via `options.LeasingScopeOptions.*`
**What goes wrong:** Lines 51–57 of `ServiceCollectionExtensions.cs`:
```csharp
services.Add(
    ServiceDescriptor.Describe(
        typeof(IAtomizerLeasingScopeFactory),
        options.LeasingScopeOptions.LockProviderFactory,
        options.LeasingScopeOptions.LockProviderLifetime
    )
);
```
These lines must be deleted entirely; they reference both `IAtomizerLeasingScopeFactory` and `LeasingScopeOptions`.
**How to avoid:** Delete the entire `services.Add(ServiceDescriptor.Describe(typeof(IAtomizerLeasingScopeFactory), ...))` block.

---

## Code Examples

Verified patterns from direct codebase inspection:

### Current XML doc style on `IAtomizerStorage` (to match)
```csharp
// Source: src/Atomizer/Abstractions/IAtomizerStorage.cs
/// <summary>
/// Inserts a new Atomizer job into the storage and returns its unique identifier.
/// </summary>
/// <param name="job">The Atomizer job to be inserted.</param>
/// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
/// <returns>The unique identifier of the inserted job.</returns>
Task<Guid> InsertAsync(AtomizerJob job, CancellationToken cancellationToken);
```

### New `ExecuteInLeaseAsync` members on `IAtomizerStorage`
```csharp
// Source: CONTEXT.md + IAtomizerStorage.cs pattern
/// <summary>
/// Executes the specified callback within an exclusive lease for the given queue.
/// The backend acquires its lock or transaction before invoking the callback and
/// releases or commits it after the callback completes. If the callback throws,
/// the lease is rolled back or released and the exception is rethrown.
/// </summary>
/// <typeparam name="TResult">The type of value returned by the callback.</typeparam>
/// <param name="queue">The queue key identifying the lease boundary.</param>
/// <param name="callback">
/// The work to execute inside the lease. Receives a <see cref="CancellationToken"/>
/// that is cancelled if the lease expires or the host shuts down.
/// </param>
/// <param name="cancellationToken">Cancellation token to cancel the lease acquisition.</param>
/// <returns>The value returned by <paramref name="callback"/>.</returns>
Task<TResult> ExecuteInLeaseAsync<TResult>(
    QueueKey queue,
    Func<CancellationToken, Task<TResult>> callback,
    CancellationToken cancellationToken
);

/// <summary>
/// Executes the specified callback within an exclusive lease for the given queue.
/// The backend acquires its lock or transaction before invoking the callback and
/// releases or commits it after the callback completes. If the callback throws,
/// the lease is rolled back or released and the exception is rethrown.
/// </summary>
/// <param name="queue">The queue key identifying the lease boundary.</param>
/// <param name="callback">
/// The work to execute inside the lease. Receives a <see cref="CancellationToken"/>
/// that is cancelled if the lease expires or the host shuts down.
/// </param>
/// <param name="cancellationToken">Cancellation token to cancel the lease acquisition.</param>
/// <returns>A task representing the asynchronous operation.</returns>
Task ExecuteInLeaseAsync(
    QueueKey queue,
    Func<CancellationToken, Task> callback,
    CancellationToken cancellationToken
);
```

### Updated `IAtomizerServiceScope` (after removing `LeasingScopeFactory`)
```csharp
// Source: src/Atomizer/Abstractions/IAtomizerServiceScope.cs (modified)
public interface IAtomizerServiceScopeFactory
{
    IAtomizerServiceScope CreateScope();
}

public interface IAtomizerServiceScope : IDisposable
{
    IAtomizerStorage Storage { get; }
    // LeasingScopeFactory removed — D-10
}
```

### Updated `ServiceProviderServiceScope` (after removing `LeasingScopeFactory`)
```csharp
// Source: src/Atomizer/Core/ServiceProviderServiceScope.cs (modified)
internal sealed class ServiceProviderServiceScope : IAtomizerServiceScope
{
    private readonly IServiceScope _scope;
    public IAtomizerStorage Storage { get; }

    public ServiceProviderServiceScope(IServiceScope scope)
    {
        _scope = scope;
        Storage = scope.ServiceProvider.GetRequiredService<IAtomizerStorage>();
        // LeasingScopeFactory resolution removed
    }

    public void Dispose() => _scope.Dispose();
}
```

### New `QueuePoller` call site (replacing the `leasingScope` block)
```csharp
// Source: QueuePoller.cs structure + D-01/D-03 decisions
if (now - _lastStorageCheck >= storageCheckInterval && itemsInChannel < queue.DegreeOfParallelism)
{
    using var scope = _serviceScopeFactory.CreateScope();
    _lastStorageCheck = now;
    var storage = scope.Storage;

    leasedJobs = await storage.ExecuteInLeaseAsync(queue.QueueKey, async innerCt =>
    {
        var jobs = await storage.GetDueJobsAsync(queue.QueueKey, now, queue.BatchSize, innerCt);
        var acquired = new List<AtomizerJob>();

        if (jobs.Count > 0)
        {
            _logger.LogDebug("Queue '{Queue}' leasing {JobCount} job(s)", queue.QueueKey, jobs.Count);
            foreach (var job in jobs)
            {
                job.Lease(leaseToken, now, queue.VisibilityTimeout);
                acquired.Add(job);
            }
            await storage.UpdateJobsAsync(acquired, innerCt);
        }
        else
        {
            _logger.LogDebug("Queue '{Queue}' found no jobs to lease", queue.QueueKey);
        }

        return acquired;
    }, ct);
}
// Channel write loop remains outside the callback, same as before
```

### New `SchedulePoller` call site (non-generic overload)
```csharp
// Source: SchedulePoller.cs structure + D-02 decision
if (now - _lastStorageCheck >= _options.StorageCheckInterval)
{
    _lastStorageCheck = now;
    var horizon = now + _options.ScheduleLeadTime!.Value;

    using var scope = _serviceScopeFactory.CreateScope();
    var storage = scope.Storage;

    await storage.ExecuteInLeaseAsync(QueueKey.Scheduler, async innerCt =>
    {
        var dueSchedules = await storage.GetDueSchedulesAsync(horizon, ioToken);

        foreach (var schedule in dueSchedules)
        {
            if (schedule.PayloadType is null)
            {
                _logger.LogWarning(
                    "Schedule {ScheduleKey} has no payload type defined, disabling schedule",
                    schedule.JobKey
                );
                schedule.Disable(now);
                continue;
            }

            await _scheduleProcessor.ProcessAsync(schedule, horizon, execToken);
            schedule.UpdateNextOccurence(horizon, now);
        }

        await storage.UpdateSchedulesAsync(dueSchedules, execToken);
    }, execToken);
}
```

### `InMemoryStorage` stub (Phase 1)
```csharp
// Source: D-05
public Task<TResult> ExecuteInLeaseAsync<TResult>(
    QueueKey queue,
    Func<CancellationToken, Task<TResult>> callback,
    CancellationToken cancellationToken
)
{
    // TODO: Implemented in Phase 2
    throw new NotImplementedException();
}

public Task ExecuteInLeaseAsync(
    QueueKey queue,
    Func<CancellationToken, Task> callback,
    CancellationToken cancellationToken
)
{
    // TODO: Implemented in Phase 2
    throw new NotImplementedException();
}
```

### `EntityFrameworkCoreStorage` stub (Phase 1)
```csharp
// Source: D-05
public Task<TResult> ExecuteInLeaseAsync<TResult>(
    QueueKey queue,
    Func<CancellationToken, Task<TResult>> callback,
    CancellationToken cancellationToken
)
{
    // TODO: Implemented in Phase 4
    throw new NotImplementedException();
}

public Task ExecuteInLeaseAsync(
    QueueKey queue,
    Func<CancellationToken, Task> callback,
    CancellationToken cancellationToken
)
{
    // TODO: Implemented in Phase 4
    throw new NotImplementedException();
}
```

---

## Complete Delete and Modify Inventory

### Files to DELETE (Phase 1)

| File | Reason |
|------|--------|
| `src/Atomizer/Abstractions/IAtomizerLeasingScope.cs` | Entire abstraction removed (D-09) |
| `src/Atomizer/Abstractions/IAtomizerLeasingScopeFactory.cs` | Entire abstraction removed (D-09) |
| `src/Atomizer/Core/NoopLeasingScopeFactory.cs` | No-op locking concept removed (D-09) |
| `src/Atomizer/Configuration/LeasingScopeOptions.cs` | Options class for deleted abstraction |

### Files to MODIFY (Phase 1)

| File | Change |
|------|--------|
| `src/Atomizer/Abstractions/IAtomizerStorage.cs` | Add `ExecuteInLeaseAsync<TResult>` + `ExecuteInLeaseAsync` (non-generic) with XML docs |
| `src/Atomizer/Abstractions/IAtomizerServiceScope.cs` | Remove `IAtomizerLeasingScopeFactory LeasingScopeFactory { get; }` property |
| `src/Atomizer/Core/ServiceProviderServiceScope.cs` | Remove `LeasingScopeFactory` property and `GetRequiredService<IAtomizerLeasingScopeFactory>()` call |
| `src/Atomizer/Configuration/AtomizerOptions.cs` | Remove `LeasingScopeOptions LeasingScopeOptions` field and its default value (references both deleted types) |
| `src/Atomizer/Configuration/ServiceCollectionExtensions.cs` | Remove `services.Add(ServiceDescriptor.Describe(typeof(IAtomizerLeasingScopeFactory), ...))` block (lines 50–57) |
| `src/Atomizer/Configuration/AtomizerOptionsExtensions.cs` | Remove `options.LeasingScopeOptions = new LeasingScopeOptions(...)` assignment in `UseInMemoryStorage` |
| `src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs` | Remove `options.LeasingScopeOptions = new LeasingScopeOptions(...)` assignment in `UseEntityFrameworkCoreStorage` |
| `src/Atomizer/Processing/QueuePoller.cs` | Replace `leasingScopeFactory` + `leasingScope` + `Acquired` block with `ExecuteInLeaseAsync` call |
| `src/Atomizer/Scheduling/SchedulePoller.cs` | Replace `leasingScopeFactory` + `leasingScope` + `Acquired` block with `ExecuteInLeaseAsync` call |
| `src/Atomizer/Storage/InMemoryStorage.cs` | Add `ExecuteInLeaseAsync` stubs (both overloads) throwing `NotImplementedException` |
| `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` | Add `ExecuteInLeaseAsync` stubs (both overloads) throwing `NotImplementedException` |
| `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs` | Remove `NoopLeasingScopeFactory` usage from non-relational fallback (Pitfall 8) |

### Files that SURVIVE UNCHANGED (Phase 1)

| File | Why it survives |
|------|----------------|
| `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs` | Deleted in Phase 5 per ROADMAP.md |
| `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScope.cs` | Deleted in Phase 5 |
| `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs` | Deleted in Phase 5 (but has Pitfall 8 — `NoopLeasingScopeFactory` reference must be inlined/removed) |

---

## Test Impact Analysis

### Tests that will BREAK after Phase 1 (intentional — D-06)

These tests use the old `IAtomizerLeasingScopeFactory` / `IAtomizerLeasingScope` mock pattern and will need to be rewritten in Phase 2/4. They do not block the Phase 1 build, but they will fail at runtime because `ExecuteInLeaseAsync` throws `NotImplementedException`.

| Test File | What breaks | Phase it's fixed |
|-----------|-------------|-----------------|
| `tests/Atomizer.Tests/Processing/QueuePollerTests.cs` | Mocks `IAtomizerLeasingScopeFactory` and `IAtomizerLeasingScope`; `scope.LeasingScopeFactory` no longer exists | Phase 2 |
| `tests/Atomizer.Tests/Scheduling/SchedulePollerTests.cs` | Same pattern — mocks `IAtomizerLeasingScopeFactory`, `IAtomizerLeasingScope`, `leasingScope.Acquired` | Phase 2 |

### Tests that will FAIL TO COMPILE after Phase 1 (must be updated)

These tests directly instantiate or reference types being deleted — they will cause compile errors and must be updated in Phase 1:

| Test File | What breaks | Fix |
|-----------|-------------|-----|
| `tests/Atomizer.Tests/Core/NoopLeasingScopeFactoryTests.cs` | References `NoopLeasingScopeFactory` which is deleted | Delete this test file in Phase 1 |
| `tests/Atomizer.Tests/Storage/InMemoryLeasingScopeFactoryTests.cs` | References `InMemoryLeasingScopeFactory` — **survives** until Phase 5, so this compiles | No action needed in Phase 1 |
| `tests/Atomizer.EntityFrameworkCore.Tests/Storage/DatabaseTransactionLeasingScopeFactoryTests.cs` | References `DatabaseTransactionLeasingScopeFactory` — **survives** until Phase 5 | No action needed in Phase 1 |

**Important:** `NoopLeasingScopeFactoryTests.cs` will cause a compile error because `NoopLeasingScopeFactory` is deleted. This test file must be deleted in Phase 1. The `InMemoryLeasingScopeFactory` and `DatabaseTransactionLeasingScopeFactory` test files survive because those implementations are not deleted until Phase 5.

Additionally, `QueuePollerTests.cs` and `SchedulePollerTests.cs` reference `IAtomizerLeasingScopeFactory` and `IAtomizerLeasingScope` directly via NSubstitute mocks. After Phase 1 deletes those interfaces, these test files will also fail to compile and must be updated in Phase 1.

---

## netstandard2.0 Compatibility Verification

**Question:** Are there any `netstandard2.0` compatibility concerns with `Func<CancellationToken, Task<TResult>>` on an interface?

**Answer:** None. [VERIFIED: codebase inspection + language spec knowledge]

- Generic methods on interfaces (`Task<TResult> ExecuteInLeaseAsync<TResult>(...)`) are fully supported in C# 2.0+ and `netstandard2.0`. No `#if` guards needed.
- `Func<CancellationToken, Task<TResult>>` is `System.Func<T, TResult>` from `mscorlib` — available since .NET 3.5, fully present in `netstandard2.0`.
- `#if NETCOREAPP3_0_OR_GREATER` is only needed for `IAsyncDisposable` and `await using` patterns (as already documented in `IAtomizerLeasingScope.cs`). The new `ExecuteInLeaseAsync` members return `Task`, not `IAsyncDisposable`, so no guards are needed.

The existing project already uses generic interface methods throughout (`IAtomizerJob<TPayload>`, `IAtomizerStorage` methods returning `Task<T>`) without any `#if` guards. [VERIFIED: codebase inspection]

---

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit v3 `2.0.1` |
| Config file | `tests/Atomizer.Tests/Atomizer.Tests.csproj` |
| Quick run command | `dotnet test tests/Atomizer.Tests/Atomizer.Tests.csproj --filter "FullyQualifiedName~QueuePoller\|FullyQualifiedName~SchedulePoller\|FullyQualifiedName~NoopLeasing" -x` |
| Full suite command | `dotnet test Atomizer.sln` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Status |
|--------|----------|-----------|-------------------|-------------|
| LEASE-01 | `ExecuteInLeaseAsync` exists on `IAtomizerStorage` | unit (compile-time) | `dotnet build` | Verified by build |
| LEASE-01 | `QueuePoller` calls `ExecuteInLeaseAsync` | unit | `dotnet test --filter "QueuePollerTests"` | Tests require rewrite (see Test Impact) |
| LEASE-02 | `IAtomizerLeasingScopeFactory` absent from compiled assembly | unit (compile-time) | `dotnet build` | Verified by build |
| LEASE-02 | `IAtomizerLeasingScope` absent from compiled assembly | unit (compile-time) | `dotnet build` | Verified by build |
| LEASE-03 | Interface signature contains no SQL primitives | code review | Manual inspection | No test needed |
| COMPAT-01 | Both storage implementations compile with new interface | unit (compile-time) | `dotnet build` | Verified by build |

### Sampling Rate

- **Per task commit:** `dotnet build Atomizer.sln --no-restore` (fast, verifies no compile errors)
- **Per wave merge:** `dotnet test tests/Atomizer.Tests/Atomizer.Tests.csproj`
- **Phase gate:** Full suite green on non-broken tests before `/gsd-verify-work`

### Wave 0 Gaps

None — no new test files needed in Phase 1. Test file updates (rewriting `QueuePollerTests`, `SchedulePollerTests`, deleting `NoopLeasingScopeFactoryTests`) are part of the implementation tasks, not Wave 0 setup.

---

## Project Constraints (from CLAUDE.md)

All of the following apply to every edit in Phase 1:

| Directive | Impact on Phase 1 |
|-----------|-------------------|
| `System.Text.Json` only — no Newtonsoft | N/A (no serialization changes) |
| CSharpier formatting (`dotnet csharpier .`) | Run after all edits; `printWidth: 120`, `indentSize: 4`, `useTabs: false` |
| XML documentation required on all public APIs | Both `ExecuteInLeaseAsync` overloads on `IAtomizerStorage` (public interface) need full XML docs |
| `TreatWarningsAsErrors=true` on `src/` projects | Missing XML docs will fail the build — docs are mandatory, not optional |
| `LangVersion=14` for `src/`, `12` for `tests/` | No language version concerns for this refactor |
| `netstandard2.0` multi-target for `src/Atomizer` | No `#if` guards needed for new members (confirmed above) |
| File-scoped namespaces | All modified/new code must use `namespace Foo;` not `namespace Foo { }` |
| `internal sealed class` for implementations | `InMemoryStorage` and `EntityFrameworkCoreStorage` are already `internal sealed` / `internal sealed` |
| Domain methods for state transitions | N/A — no domain model changes |
| `CancellationToken cancellationToken` as last parameter, no default on internal APIs | Confirmed: `IAtomizerStorage` is internal-facing; use `cancellationToken` without default |
| Conventional commits | `refactor(leasing): ...` or `feat(storage): ...` per CLAUDE.md commit style |
| One public type per file | Files being deleted had one type each; surviving files unchanged |

---

## Open Questions

1. **`DatabaseTransactionLeasingScopeFactory` non-relational fallback (Pitfall 8)**
   - What we know: Line 37 calls `new NoopLeasingScopeFactory()` for non-relational providers. `NoopLeasingScopeFactory` is deleted in Phase 1.
   - What's unclear: Should the non-relational branch throw `NotSupportedException`, or should the noop behavior be inlined directly?
   - Recommendation: Since `DatabaseTransactionLeasingScopeFactory` is deleted entirely in Phase 5, the simplest fix for Phase 1 is to inline the noop behavior directly (create a small private `NoopLeasingScope` inner class, or just throw `NotSupportedException` for non-relational since the EF Core package shouldn't be used with SQLite in production). Given that `InMemoryStorage` is the production non-relational backend, throwing `NotSupportedException` for non-relational in `DatabaseTransactionLeasingScopeFactory` is acceptable.

2. **Non-generic overload delegation vs. independent implementation**
   - What we know: Claude's Discretion (CONTEXT.md).
   - What's unclear: Whether `Task ExecuteInLeaseAsync(...)` delegates to `ExecuteInLeaseAsync<object?>(...)` or implements independently.
   - Recommendation: In the stubs (Phase 1), both overloads simply `throw new NotImplementedException()` — delegation is irrelevant. For the interface definition, both overloads are independent members (no default implementation, as `IAtomizerStorage` is a pure interface without `default` members).

---

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|------------------|--------|
| Two-step acquire/release: `CreateScopeAsync` returns `IAtomizerLeasingScope { Acquired }`, caller checks flag | Single callback: `ExecuteInLeaseAsync(queue, callback, ct)` — lease is invisible to caller | Eliminates the `if (Acquired)` guard, prevents double-dispatch, makes backends truly pluggable |
| Separate `IAtomizerLeasingScopeFactory` injected alongside `IAtomizerStorage` | Leasing is owned entirely by `IAtomizerStorage` — `ExecuteInLeaseAsync` is a first-class storage operation | One interface, one injection point; new backends only implement `IAtomizerStorage` |

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `InMemoryLeasingScopeFactory`, `DatabaseTransactionLeasingScope`, and `DatabaseTransactionLeasingScopeFactory` are NOT deleted in Phase 1 (only in Phase 5) | Delete Inventory | If the planner deletes them in Phase 1, test files referencing them will break unnecessarily early |

**Note:** A1 is based on ROADMAP.md Phase 5 success criteria listing those types explicitly. CONTEXT.md "Types Being Deleted" lists only `NoopLeasingScopeFactory.cs` and the two abstract interfaces for Phase 1. [CITED: .planning/ROADMAP.md Phase 5, .planning/phases/01-leasing-abstraction/01-CONTEXT.md]

---

## Sources

### Primary (HIGH confidence — direct codebase inspection)

- `src/Atomizer/Abstractions/IAtomizerStorage.cs` — current interface, all existing members and XML doc style
- `src/Atomizer/Abstractions/IAtomizerLeasingScopeFactory.cs` — interface being deleted
- `src/Atomizer/Abstractions/IAtomizerLeasingScope.cs` — interface being deleted
- `src/Atomizer/Abstractions/IAtomizerServiceScope.cs` — property being removed
- `src/Atomizer/Processing/QueuePoller.cs` — full call site analysis, `leasedJobs` placement
- `src/Atomizer/Scheduling/SchedulePoller.cs` — full call site analysis
- `src/Atomizer/Core/NoopLeasingScopeFactory.cs` — file being deleted
- `src/Atomizer/Core/ServiceProviderServiceScope.cs` — property resolution being removed
- `src/Atomizer/Configuration/AtomizerOptions.cs` — `LeasingScopeOptions` field being removed
- `src/Atomizer/Configuration/LeasingScopeOptions.cs` — file being deleted
- `src/Atomizer/Configuration/ServiceCollectionExtensions.cs` — DI registration being removed
- `src/Atomizer/Configuration/AtomizerOptionsExtensions.cs` — `LeasingScopeOptions` assignment being removed
- `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` — stub target
- `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs` — `NoopLeasingScopeFactory` reference (Pitfall 8)
- `src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs` — `LeasingScopeOptions` assignment being removed
- `src/Atomizer/Storage/InMemoryStorage.cs` — stub target
- `src/Atomizer/Processing/QueuePump.cs` — confirmed no `LeasingScopeFactory` usage in `StopAsync`
- `tests/Atomizer.Tests/Processing/QueuePollerTests.cs` — compile-breaking test analysis
- `tests/Atomizer.Tests/Scheduling/SchedulePollerTests.cs` — compile-breaking test analysis
- `tests/Atomizer.Tests/Core/NoopLeasingScopeFactoryTests.cs` — must be deleted
- `tests/Atomizer.Tests/Storage/InMemoryLeasingScopeFactoryTests.cs` — survives Phase 1
- `tests/Atomizer.EntityFrameworkCore.Tests/Storage/DatabaseTransactionLeasingScopeFactoryTests.cs` — survives Phase 1
- `.planning/phases/01-leasing-abstraction/01-CONTEXT.md` — locked decisions
- `.planning/ROADMAP.md` — phase scope and Phase 5 deletion list

### Secondary (MEDIUM confidence)

- `dotnet build` output — 0 errors, 38 suppressed vulnerability warnings — confirms clean baseline

---

## Metadata

**Confidence breakdown:**
- Delete inventory: HIGH — all files inspected directly
- Call site analysis: HIGH — both pollers read in full
- Test impact: HIGH — all affected test files read in full
- netstandard2.0 compatibility: HIGH — verified against existing codebase patterns
- `DatabaseTransactionLeasingScopeFactory` Pitfall 8: HIGH — line 37 directly observed

**Research date:** 2026-05-03
**Valid until:** This is a pure codebase-derived research; valid until code changes (no expiry for external library drift)
