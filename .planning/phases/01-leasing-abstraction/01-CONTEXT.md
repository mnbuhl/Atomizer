# Phase 1: Leasing Abstraction - Context

**Gathered:** 2026-05-03
**Status:** Ready for planning

<domain>
## Phase Boundary

Define the `ExecuteInLeaseAsync` contract on `IAtomizerStorage`, remove `IAtomizerLeasingScopeFactory` / `IAtomizerLeasingScope` from the public API, update `QueuePoller` and `SchedulePoller` to the new call site, and stub both `InMemoryStorage` and `EntityFrameworkCoreStorage` with `NotImplementedException`. Phase 1 delivers a clean, provider-agnostic interface shape — real backend implementations land in Phases 2 (InMemory) and 4 (EF Core).

</domain>

<decisions>
## Implementation Decisions

### Callback Shape

- **D-01:** `ExecuteInLeaseAsync` is **generic** — signature: `Task<TResult> ExecuteInLeaseAsync<TResult>(QueueKey queue, Func<CancellationToken, Task<TResult>> callback, CancellationToken ct)`. Callers receive the return value directly; no closure/shared mutable state needed.
- **D-02:** A **non-generic overload** also exists: `Task ExecuteInLeaseAsync(QueueKey queue, Func<CancellationToken, Task> callback, CancellationToken ct)`. `SchedulePoller` and any caller with no return value uses this.
- **D-03:** `GetDueJobsAsync` / `GetDueSchedulesAsync` are **called by the caller from inside the callback** — storage only manages the lease scope, not what happens inside it. The contract stays open and backend-agnostic.
- **D-04:** If the callback throws, `ExecuteInLeaseAsync` **rolls back / releases the lease** (EF Core rolls back transaction; InMemory releases semaphore) and **rethrows** the exception unchanged to the caller.

### Stub Strategy

- **D-05:** Both `InMemoryStorage.ExecuteInLeaseAsync` and `EntityFrameworkCoreStorage.ExecuteInLeaseAsync` throw `NotImplementedException` in Phase 1, annotated with a TODO pointing to the implementing phase:
  - `// TODO: Implemented in Phase 2`
  - `// TODO: Implemented in Phase 4`
- **D-06:** `QueuePoller` and `SchedulePoller` are **updated to the new call site in Phase 1** (ROADMAP.md success criterion #4). The stubs throw, so existing integration tests that exercise the poll path will fail until Phase 2/4 — this is intentional and expected.

### ReleaseLeasedAsync

- **D-07:** `ReleaseLeasedAsync(LeaseToken, DateTimeOffset, CancellationToken)` **survives unchanged on `IAtomizerStorage`** in Phase 1. It is a shutdown recovery mechanism, not part of the leasing scope abstraction being replaced.
- **D-08:** `QueuePump` continues to hold its own `LeaseToken` per-pump (already the existing pattern). `GetDueJobsAsync` called inside `ExecuteInLeaseAsync` stamps jobs with this token. `StopAsync` calls `ReleaseLeasedAsync` with the same token. No change to the shutdown path.

### Noop / IAtomizerLeasingScopeFactory Removal

- **D-09:** `IAtomizerLeasingScopeFactory`, `IAtomizerLeasingScope`, and `NoopLeasingScopeFactory` are **all deleted in Phase 1**. There is no "no-op lock" concept after the refactor — every backend owns its atomicity strategy inside `ExecuteInLeaseAsync`.
- **D-10:** `IAtomizerServiceScope.LeasingScopeFactory` property is **removed**. The service scope only needs to expose `Storage`. `QueuePoller` and `SchedulePoller` call `scope.Storage.ExecuteInLeaseAsync(...)` directly.

### Claude's Discretion

- Exact XML documentation wording on the new interface members.
- Whether both overloads live on the same interface or one delegates to the other internally.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Interface Files Being Changed
- `src/Atomizer/Abstractions/IAtomizerStorage.cs` — Current interface; add `ExecuteInLeaseAsync` overloads here
- `src/Atomizer/Abstractions/IAtomizerLeasingScopeFactory.cs` — Deleted in this phase
- `src/Atomizer/Abstractions/IAtomizerLeasingScope.cs` — Deleted in this phase

### Callers Being Updated
- `src/Atomizer/Processing/QueuePoller.cs` — Updates to `ExecuteInLeaseAsync` call site; remove `leasingScope.Acquired` pattern
- `src/Atomizer/Scheduling/SchedulePoller.cs` — Same update, uses non-generic overload

### Storage Stubs
- `src/Atomizer/Storage/InMemoryStorage.cs` — Add `NotImplementedException` stub (Phase 2 owns real impl)
- `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` — Add `NotImplementedException` stub (Phase 4 owns real impl)

### Types Being Deleted
- `src/Atomizer/Core/NoopLeasingScopeFactory.cs` — Deleted in this phase
- `src/Atomizer/Abstractions/IAtomizerServiceScope.cs` — Remove `LeasingScopeFactory` property

### Requirements and Roadmap
- `.planning/REQUIREMENTS.md` — Requirements LEASE-01, LEASE-02, LEASE-03, COMPAT-01 govern Phase 1
- `.planning/ROADMAP.md` — Phase 1 success criteria (4 items) are the acceptance test

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `LeaseToken` value object (`src/Atomizer/Models/ValueObjects/LeaseToken.cs`) — survives unchanged; still used by `QueuePump` + `ReleaseLeasedAsync`
- `IAtomizerServiceScopeFactory` / `IAtomizerServiceScope` — survives; `LeasingScopeFactory` property removed, `Storage` property kept

### Established Patterns
- `#if NETCOREAPP3_0_OR_GREATER` guard — required in `IAtomizerStorage` if any new member uses `IAsyncDisposable`; not needed for `ExecuteInLeaseAsync` since it returns `Task`
- XML `<summary>` + `<param>` + `<returns>` on all public interface members — enforced by `TreatWarningsAsErrors=true`
- File-scoped namespaces, `internal sealed class` for implementations, `public interface` for abstractions
- `CancellationToken cancellationToken` is always the last parameter (no default on internal APIs; `cancellation = default` on public client APIs — the storage interface is internal-facing so use `cancellationToken` without default)

### Integration Points
- `QueuePoller` resolves `scope.Storage` and `scope.LeasingScopeFactory` — after this phase it only resolves `scope.Storage`
- `SchedulePoller` same pattern
- DI registration in `src/Atomizer/Configuration/ServiceCollectionExtensions.cs` — `IAtomizerLeasingScopeFactory` registration must be removed; `NoopLeasingScopeFactory` default registration removed
- `LeasingScopeOptions.cs` in `src/Atomizer/Configuration/` — may also be deleted if it only configures the factory

</code_context>

<specifics>
## Specific Ideas

- The generic overload signature agreed in discussion:
  ```csharp
  Task<TResult> ExecuteInLeaseAsync<TResult>(
      QueueKey queue,
      Func<CancellationToken, Task<TResult>> callback,
      CancellationToken cancellationToken
  );
  ```
- The non-generic overload:
  ```csharp
  Task ExecuteInLeaseAsync(
      QueueKey queue,
      Func<CancellationToken, Task> callback,
      CancellationToken cancellationToken
  );
  ```
- Stub body example:
  ```csharp
  // TODO: Implemented in Phase 2
  throw new NotImplementedException();
  ```

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 1-Leasing Abstraction*
*Context gathered: 2026-05-03*
