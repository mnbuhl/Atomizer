---
phase: 01-leasing-abstraction
plan: "02"
subsystem: configuration, storage, processing, scheduling
tags: [breaking-change, refactor, di-cleanup, build-fix, stubs]
dependency_graph:
  requires: [01-01]
  provides: [clean-build, ExecuteInLeaseAsync stubs, DI cleanup]
  affects:
    - src/Atomizer/Configuration/AtomizerOptions.cs
    - src/Atomizer/Configuration/ServiceCollectionExtensions.cs
    - src/Atomizer/Configuration/AtomizerOptionsExtensions.cs
    - src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs
    - src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs
    - src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScope.cs
    - src/Atomizer/Storage/InMemoryStorage.cs
    - src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs
    - src/Atomizer/Processing/QueuePoller.cs
    - src/Atomizer/Scheduling/SchedulePoller.cs
tech_stack:
  added: []
  patterns: [callback-based leasing via ExecuteInLeaseAsync, NotImplementedException stubs for phased implementation]
key_files:
  created: []
  modified:
    - src/Atomizer/Configuration/AtomizerOptions.cs
    - src/Atomizer/Configuration/ServiceCollectionExtensions.cs
    - src/Atomizer/Configuration/AtomizerOptionsExtensions.cs
    - src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs
    - src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs
    - src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScope.cs
    - src/Atomizer/Storage/InMemoryStorage.cs
    - src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs
    - src/Atomizer/Processing/QueuePoller.cs
    - src/Atomizer/Scheduling/SchedulePoller.cs
    - tests/Atomizer.Tests/Processing/QueuePollerTests.cs
    - tests/Atomizer.Tests/Scheduling/SchedulePollerTests.cs
    - tests/Atomizer.EntityFrameworkCore.Tests/Storage/DatabaseTransactionLeasingScopeFactoryTests.cs
  deleted:
    - tests/Atomizer.Tests/Core/NoopLeasingScopeFactoryTests.cs
decisions:
  - "QueuePoller and SchedulePoller rewritten to call storage.ExecuteInLeaseAsync instead of deleted scope factory pattern — Phase-5 callers eliminated"
  - "Phase-5 survivor files (InMemoryLeasingScopeFactory, DatabaseTransactionLeasingScope, DatabaseTransactionLeasingScopeFactory) stripped of deleted interface references; class structure preserved for Phase 5 rewire"
  - "DatabaseTransactionLeasingScopeFactory non-relational fallback now throws NotSupportedException instead of using deleted NoopLeasingScopeFactory"
  - "NoopLeasingScopeFactoryTests.cs deleted — the tested class was deleted in Plan 01"
metrics:
  duration: "25 minutes"
  completed: "2026-05-03"
  tasks_completed: 2
  tasks_total: 2
  files_changed: 14
---

# Phase 1 Plan 02: Fix Build After Leasing Abstraction Removal Summary

Removed all compile-time references to the deleted leasing types from DI/configuration, patched Phase-5 survivor files to compile without the deleted interfaces, and added NotImplementedException stubs to both storage backends — bringing `dotnet build Atomizer.sln` to exit 0.

## What Was Done

### Task 1: Remove LeasingScopeOptions from AtomizerOptions and DI registrations (commit 37323b3)

- `AtomizerOptions.cs` — removed `LeasingScopeOptions` field and `NoopLeasingScopeFactory` default; removed now-unused `using Atomizer.Core;`
- `ServiceCollectionExtensions.cs` — removed the 7-line `IAtomizerLeasingScopeFactory` registration block
- `src/Atomizer/Configuration/AtomizerOptionsExtensions.cs` — removed `options.LeasingScopeOptions = new LeasingScopeOptions(...)` assignment
- `src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs` — removed `options.LeasingScopeOptions = new LeasingScopeOptions(...)` assignment; removed now-unused `using Atomizer.Core;`

### Task 2: Patch Phase-5 survivor files and add ExecuteInLeaseAsync stubs (commit 4f4de6b)

- `InMemoryStorage.cs` — added both `ExecuteInLeaseAsync` stubs with `// TODO: Implemented in Phase 2`
- `EntityFrameworkCoreStorage.cs` — added both `ExecuteInLeaseAsync` stubs with `// TODO: Implemented in Phase 4`
- `DatabaseTransactionLeasingScopeFactory.cs` — replaced `NoopLeasingScopeFactory` fallback with `NotSupportedException`; removed `IAtomizerLeasingScopeFactory` interface implementation; return type changed from `IAtomizerLeasingScope` to `DatabaseTransactionLeasingScope`
- `DatabaseTransactionLeasingScope.cs` — removed `IAtomizerLeasingScope` interface implementation; now implements `IDisposable, IAsyncDisposable` directly; `StartTransaction` return type changed to `DatabaseTransactionLeasingScope`
- `InMemoryLeasingScopeFactory.cs` — removed `IAtomizerLeasingScopeFactory` and `IAtomizerLeasingScope` interface references; inner class changed to `internal sealed class InMemoryLeasingScope : IDisposable` with `#if NETCOREAPP3_0_OR_GREATER` guard for `IAsyncDisposable`
- `QueuePoller.cs` — rewired from `leasingScopeFactory.CreateScopeAsync(...)` pattern to `storage.ExecuteInLeaseAsync(...)` callback pattern
- `SchedulePoller.cs` — same rewiring as QueuePoller

### Test updates (included in commit 4f4de6b)

- `QueuePollerTests.cs` — removed `IAtomizerLeasingScopeFactory`/`IAtomizerLeasingScope` mocks; added `storage.ExecuteInLeaseAsync` stub that invokes the callback inline
- `SchedulePollerTests.cs` — same pattern; removed `scope.LeasingScopeFactory` setup; added `storage.ExecuteInLeaseAsync` stub
- `DatabaseTransactionLeasingScopeFactoryTests.cs` — changed `List<IAtomizerLeasingScope>` to `List<DatabaseTransactionLeasingScope>`; removed `using Atomizer.Abstractions;`
- `NoopLeasingScopeFactoryTests.cs` — deleted (class was deleted in Plan 01)

## Verification Results

- `grep -r "LeasingScopeOptions" src/` — no output (all references removed)
- `grep -r "IAtomizerLeasingScopeFactory" src/` — no output (all references removed)
- `grep -c "ExecuteInLeaseAsync" src/Atomizer/Storage/InMemoryStorage.cs` — **2**
- `grep -c "ExecuteInLeaseAsync" src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` — **2**
- `grep "NotSupportedException" src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs` — present
- `dotnet build Atomizer.sln` — **Build succeeded**

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] InMemoryLeasingScopeFactory.cs still referenced deleted interfaces**
- **Found during:** Task 1 build attempt
- **Issue:** `InMemoryLeasingScopeFactory` implemented `IAtomizerLeasingScopeFactory` and used `IAtomizerLeasingScope` — both deleted in Plan 01. Plan 01 SUMMARY noted this was a known outstanding error.
- **Fix:** Removed interface implementations; changed class to standalone `internal sealed class`; changed `CreateScopeAsync` return type to `Task<InMemoryLeasingScope>`; added `#if NETCOREAPP3_0_OR_GREATER` guard for `IAsyncDisposable` per CLAUDE.md multi-targeting conventions
- **Files modified:** `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs`
- **Commit:** 4f4de6b

**2. [Rule 3 - Blocking] DatabaseTransactionLeasingScope.cs and DatabaseTransactionLeasingScopeFactory.cs referenced deleted interfaces**
- **Found during:** Task 2 analysis
- **Issue:** Both EF Core Phase-5 survivor files implemented/returned `IAtomizerLeasingScope`/`IAtomizerLeasingScopeFactory`
- **Fix:** Stripped interface implementations; changed concrete return types; removed `using Atomizer.Abstractions;` and `using Atomizer.Core;`
- **Files modified:** `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScope.cs`, `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs`
- **Commit:** 4f4de6b

**3. [Rule 3 - Blocking] QueuePoller.cs and SchedulePoller.cs used scope.LeasingScopeFactory**
- **Found during:** Task 1 build attempt
- **Issue:** Both pollers called `scope.LeasingScopeFactory` which was removed from `IAtomizerServiceScope` in Plan 01
- **Fix:** Rewrote polling logic to use `storage.ExecuteInLeaseAsync(...)` callback pattern — this is the correct post-refactor pattern
- **Files modified:** `src/Atomizer/Processing/QueuePoller.cs`, `src/Atomizer/Scheduling/SchedulePoller.cs`
- **Commit:** 4f4de6b

**4. [Rule 3 - Blocking] Test files referenced deleted types**
- **Found during:** Task 2 full solution build
- **Issue:** `QueuePollerTests.cs`, `SchedulePollerTests.cs`, `DatabaseTransactionLeasingScopeFactoryTests.cs`, and `NoopLeasingScopeFactoryTests.cs` all referenced deleted types
- **Fix:** Rewrote tests to use `ExecuteInLeaseAsync` mocking; deleted `NoopLeasingScopeFactoryTests.cs`
- **Files modified/deleted:** 3 test files updated, 1 deleted
- **Commit:** 4f4de6b

## Known Stubs

| Stub | File | Line | Reason |
|------|------|------|--------|
| `ExecuteInLeaseAsync<TResult>` | `src/Atomizer/Storage/InMemoryStorage.cs` | ~222 | Phase 2 will implement real in-memory leasing |
| `ExecuteInLeaseAsync` | `src/Atomizer/Storage/InMemoryStorage.cs` | ~231 | Phase 2 will implement real in-memory leasing |
| `ExecuteInLeaseAsync<TResult>` | `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` | ~243 | Phase 4 will implement EF Core transaction-based leasing |
| `ExecuteInLeaseAsync` | `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` | ~252 | Phase 4 will implement EF Core transaction-based leasing |

These stubs are intentional — pollers calling them will throw `NotImplementedException` until Phase 2/4 implement the real logic. This is by design per the plan's threat model (T-02-01, T-02-02).

## Threat Flags

No new security-relevant surface introduced. All changes are internal refactoring of existing components.

## Self-Check: PASSED

- `src/Atomizer/Configuration/AtomizerOptions.cs` — exists, no `LeasingScopeOptions`
- `src/Atomizer/Configuration/ServiceCollectionExtensions.cs` — exists, no `IAtomizerLeasingScopeFactory`
- `src/Atomizer/Storage/InMemoryStorage.cs` — exists, contains 2 `ExecuteInLeaseAsync` stubs
- `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` — exists, contains 2 `ExecuteInLeaseAsync` stubs
- `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs` — exists, contains `NotSupportedException`, no `NoopLeasingScopeFactory`
- Commits 37323b3 and 4f4de6b confirmed in git log
- `dotnet build Atomizer.sln` — Build succeeded
