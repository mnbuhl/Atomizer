# Phase 1: Leasing Abstraction - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-05-03
**Phase:** 1-Leasing Abstraction
**Areas discussed:** Callback shape, Stub strategy, ReleaseLeasedAsync fate, Noop replacement

---

## Callback Shape

### Q1: Generic vs void callback

| Option | Description | Selected |
|--------|-------------|----------|
| Generic: `Task<TResult>` | Callers get the return value directly; no shared mutable state | ✓ |
| Void: `Func<CT, Task>` | Callers capture results via closure; requires mutable state in QueuePoller | |

**User's choice:** Generic `Task<TResult>`
**Notes:** Caller calls `GetDueJobsAsync` inside the callback and returns the list directly.

---

### Q2: Who calls GetDueJobsAsync?

| Option | Description | Selected |
|--------|-------------|----------|
| Caller passes work as callback | Storage manages the lease scope; caller calls GetDueJobsAsync inside | ✓ |
| Storage calls GetDueJobs internally | Storage takes batch params and handles GetDueJobs internally | |

**User's choice:** Caller passes work as callback — backend-agnostic contract.

---

### Q3: Non-generic overload

| Option | Description | Selected |
|--------|-------------|----------|
| Non-generic overload `Task` | SchedulePoller and void callers use a clean overload | ✓ |
| Single generic, callers return dummy | Only `Task<TResult>` exists; void callers return dummy value | |

**User's choice:** Add non-generic overload.

---

### Q4: Callback exception handling

| Option | Description | Selected |
|--------|-------------|----------|
| Rollback/release then rethrow | EF Core rolls back transaction; InMemory releases semaphore; exception propagates | ✓ |
| Swallow and return default | Storage swallows exception and returns default(TResult) | |

**User's choice:** Rollback/release then rethrow.

---

## Stub Strategy

### Q1: What stubs do in Phase 1

| Option | Description | Selected |
|--------|-------------|----------|
| Throw `NotImplementedException` | Simple, loud failure if stub is called | ✓ |
| Working bridge (call old scope factory) | Temporary adapter keeps tests green but adds complexity | |
| No stub — merge Phase 1+2 | Reorder phases to avoid stub entirely | |

**User's choice:** `NotImplementedException` — honest and intentional.

---

### Q2: Update callers in Phase 1?

| Option | Description | Selected |
|--------|-------------|----------|
| Update callers in Phase 1 | QueuePoller and SchedulePoller move to ExecuteInLeaseAsync now | ✓ |
| Update callers in backend phases | Callers stay on old pattern until Phase 2/4 | |

**User's choice:** Update callers in Phase 1.

---

### Q3: TODO comment on stubs

| Option | Description | Selected |
|--------|-------------|----------|
| `// TODO: Phase 2 / Phase 4` | Annotate stub with phase that implements it | ✓ |
| No comment, just throw | Minimal; ROADMAP.md already documents ownership | |

**User's choice:** Yes — annotate with `// TODO: Implemented in Phase N`.

---

## ReleaseLeasedAsync Fate

### Q1: Survive or refactor in Phase 1?

| Option | Description | Selected |
|--------|-------------|----------|
| Survive unchanged in Phase 1 | Shutdown recovery mechanism, not part of leasing scope abstraction | ✓ |
| Refactor shutdown path too | Remove ReleaseLeasedAsync and redesign QueuePump.StopAsync | |

**User's choice:** Survive unchanged — out of scope for Phase 1.

---

### Q2: How does QueuePump still have a LeaseToken?

| Option | Description | Selected |
|--------|-------------|----------|
| QueuePump holds its own LeaseToken | Already the existing pattern; no change needed | ✓ |
| LeaseToken moves inside ExecuteInLeaseAsync | Storage generates token internally and passes it to callback | |

**User's choice:** QueuePump holds its own LeaseToken — already how it works.

---

## Noop / IAtomizerLeasingScopeFactory Removal

### Q1: What replaces the Noop?

| Option | Description | Selected |
|--------|-------------|----------|
| Noop disappears — each storage IS the implementation | No "no lock" concept; every backend always does something | ✓ |
| Replace with NoopStorage adapter | A no-op ExecuteInLeaseAsync that just calls callback directly | |

**User's choice:** Noop disappears entirely.

---

### Q2: When is NoopLeasingScopeFactory deleted?

| Option | Description | Selected |
|--------|-------------|----------|
| Delete in Phase 1 alongside IAtomizerLeasingScopeFactory | Interface and default impl deleted together | ✓ |
| Keep until Phase 5 cleanup | Leave compiling but unused | |

**User's choice:** Delete in Phase 1.

---

### Q3: IAtomizerServiceScope.LeasingScopeFactory

| Option | Description | Selected |
|--------|-------------|----------|
| Remove LeasingScopeFactory from IAtomizerServiceScope | Scope only needs Storage; QueuePoller calls storage.ExecuteInLeaseAsync directly | ✓ |
| Keep as null / throw if accessed | Leave property but make it inert | |

**User's choice:** Remove LeasingScopeFactory — simplify the service scope.

---

## Claude's Discretion

- Exact XML documentation wording on new interface members
- Whether both `ExecuteInLeaseAsync` overloads live on the interface or one delegates to the other

## Deferred Ideas

None — discussion stayed within phase scope.
