---
phase: 01-leasing-abstraction
plan: "01"
subsystem: abstractions
tags: [breaking-change, interface, leasing, refactor]
dependency_graph:
  requires: []
  provides: [ExecuteInLeaseAsync interface shape]
  affects:
    - src/Atomizer/Abstractions/IAtomizerStorage.cs
    - src/Atomizer/Abstractions/IAtomizerServiceScope.cs
    - src/Atomizer/Core/ServiceProviderServiceScope.cs
tech_stack:
  added: []
  patterns: [callback-based leasing, interface-only breaking change]
key_files:
  created: []
  modified:
    - src/Atomizer/Abstractions/IAtomizerStorage.cs
    - src/Atomizer/Abstractions/IAtomizerServiceScope.cs
    - src/Atomizer/Core/ServiceProviderServiceScope.cs
  deleted:
    - src/Atomizer/Abstractions/IAtomizerLeasingScope.cs
    - src/Atomizer/Abstractions/IAtomizerLeasingScopeFactory.cs
    - src/Atomizer/Core/NoopLeasingScopeFactory.cs
    - src/Atomizer/Configuration/LeasingScopeOptions.cs
decisions:
  - "ExecuteInLeaseAsync uses Func<CancellationToken, Task<TResult>> callback pattern — no #if guards needed as generic interface methods are netstandard2.0 compatible"
  - "LeasingScopeFactory removed from IAtomizerServiceScope — backends own their atomicity strategy entirely via ExecuteInLeaseAsync"
  - "Four files deleted via git rm — InMemoryLeasingScopeFactory and DatabaseTransaction* files intentionally preserved for Phase 5"
metrics:
  duration: "1 minute"
  completed: "2026-05-03"
  tasks_completed: 3
  tasks_total: 3
  files_changed: 7
---

# Phase 1 Plan 01: Define ExecuteInLeaseAsync Contract Summary

Defined the callback-based leasing contract on IAtomizerStorage and removed the old four-type leasing abstraction (IAtomizerLeasingScopeFactory, IAtomizerLeasingScope, NoopLeasingScopeFactory, LeasingScopeOptions) from the public API surface.

## What Was Done

### Task 1: Add ExecuteInLeaseAsync to IAtomizerStorage (commit 4e5fdfe)

Added two new interface members to `IAtomizerStorage` after `GetDueSchedulesAsync`:

- `Task<TResult> ExecuteInLeaseAsync<TResult>(QueueKey queue, Func<CancellationToken, Task<TResult>> callback, CancellationToken cancellationToken)` — generic overload with full XML docs including `<typeparam name="TResult">`
- `Task ExecuteInLeaseAsync(QueueKey queue, Func<CancellationToken, Task> callback, CancellationToken cancellationToken)` — non-generic overload with full XML docs

No `#if` guards required — generic methods on interfaces are `netstandard2.0` compatible.

### Task 2: Remove LeasingScopeFactory from IAtomizerServiceScope (commit a1d5d9a)

- Removed `IAtomizerLeasingScopeFactory LeasingScopeFactory { get; }` from `IAtomizerServiceScope` — interface now exposes only `Storage`
- Removed the `LeasingScopeFactory` property and its `GetRequiredService<IAtomizerLeasingScopeFactory>()` constructor assignment from `ServiceProviderServiceScope`

### Task 3: Delete Old Leasing Abstraction Files (commit cc46417)

Deleted four files via `git rm`:
- `src/Atomizer/Abstractions/IAtomizerLeasingScope.cs`
- `src/Atomizer/Abstractions/IAtomizerLeasingScopeFactory.cs`
- `src/Atomizer/Core/NoopLeasingScopeFactory.cs`
- `src/Atomizer/Configuration/LeasingScopeOptions.cs`

Phase-5 files intentionally preserved:
- `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs`
- `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScope.cs`
- `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs`

## Verification Results

All plan verification checks pass:
- `grep -c "Task.*ExecuteInLeaseAsync" IAtomizerStorage.cs` → **2** (both overloads present)
- `grep -c "LeasingScopeFactory" IAtomizerServiceScope.cs` → **0**
- `grep -c "IAtomizerLeasingScopeFactory" ServiceProviderServiceScope.cs` → **0**
- All four deleted files confirmed absent from disk
- Phase-5 files confirmed still present

## Build Status After Plan 01

Build fails as expected — `AtomizerOptions.cs`, `InMemoryLeasingScopeFactory.cs`, `InMemoryStorage.cs`, and `ServiceCollectionExtensions.cs` still reference deleted types. These are addressed in Plan 02. Unique error sources:

- `AtomizerOptions.cs` — references `LeasingScopeOptions` (Plan 02)
- `InMemoryLeasingScopeFactory.cs` — references `IAtomizerLeasingScopeFactory` / `IAtomizerLeasingScope` (Plan 02 / Phase 5)
- `InMemoryStorage.cs` — does not yet implement `ExecuteInLeaseAsync` (Plan 02)

No unexpected errors found.

## Deviations from Plan

None — plan executed exactly as written.

## Known Stubs

None — this plan only defines interface shape and deletes types. No data flows or UI rendering involved.

## Threat Flags

No new security-relevant surface introduced. `ExecuteInLeaseAsync` is a public interface method on an internally-owned abstraction (no external implementors at this stage, per T-01-01 threat register).

## Self-Check: PASSED

- `src/Atomizer/Abstractions/IAtomizerStorage.cs` — exists and contains both `ExecuteInLeaseAsync` overloads
- `src/Atomizer/Abstractions/IAtomizerServiceScope.cs` — exists, no `LeasingScopeFactory`
- `src/Atomizer/Core/ServiceProviderServiceScope.cs` — exists, no `IAtomizerLeasingScopeFactory`
- Four deleted files confirmed absent
- Commits 4e5fdfe, a1d5d9a, cc46417 confirmed in git log
