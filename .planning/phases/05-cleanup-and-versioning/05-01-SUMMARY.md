---
phase: 05-cleanup-and-versioning
plan: "01"
subsystem: storage/ef-core
tags: [visibility, build, xml-docs, cleanup]
dependency_graph:
  requires: []
  provides: [GenerateDocumentationFile-enforcement, internal-sealed-DatabaseTransactionLeasingScope]
  affects: [05-02, 05-03, 05-04]
tech_stack:
  added: []
  patterns: [internal-sealed-class, GenerateDocumentationFile]
key_files:
  created: []
  modified:
    - src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScope.cs
    - src/Atomizer/Atomizer.csproj
    - src/Atomizer.EntityFrameworkCore/Atomizer.EntityFrameworkCore.csproj
  deleted:
    - src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs
decisions:
  - "CS1591 errors from GenerateDocumentationFile are pre-existing and intentional; plans 02-04 address them"
  - "DatabaseTransactionLeasingScopeFactory deleted — zero callers, not DI-registered, factory is dead code (D-02)"
metrics:
  duration: "~1 minute"
  completed: "2026-05-03"
  tasks_completed: 2
  tasks_total: 2
  files_changed: 4
---

# Phase 5 Plan 01: Access Modifier and Build Config Cleanup Summary

**One-liner:** Made `DatabaseTransactionLeasingScope` internal sealed, deleted its dead factory, and enabled `GenerateDocumentationFile` enforcement on both shipping projects.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Make DatabaseTransactionLeasingScope internal sealed, delete factory | f8a9431 | DatabaseTransactionLeasingScope.cs (modified), DatabaseTransactionLeasingScopeFactory.cs (deleted) |
| 2 | Add GenerateDocumentationFile to both csproj files | 66d7839 | Atomizer.csproj, Atomizer.EntityFrameworkCore.csproj |

## What Was Built

### Task 1: Visibility fix + dead code removal

`DatabaseTransactionLeasingScope` was `public class` — externally visible despite being an internal implementation detail of the EF Core storage backend. Changed to `internal sealed class` per D-01. All method bodies, the `StartTransaction` static factory, `Acquired` property, `Abort`, `Dispose`, and `DisposeAsync` are byte-for-byte unchanged.

`DatabaseTransactionLeasingScopeFactory<TDbContext>` was deleted (D-02). Confirmed zero callers: `EntityFrameworkCoreStorage` calls `DatabaseTransactionLeasingScope.StartTransaction` directly at lines 274 and 303. The factory was not registered in DI and not referenced anywhere in the codebase.

### Task 2: Documentation file generation

Added `<GenerateDocumentationFile>true</GenerateDocumentationFile>` inside the first `<PropertyGroup>` of both shipping csproj files. This makes CS1591 (Missing XML comment for publicly visible type or member) a compile error, enforcing the XML documentation requirement (COMPAT-03) at build time. Plans 02-04 address the pre-existing CS1591 backlog.

## Deviations from Plan

None — plan executed exactly as written.

**Note on build errors:** The build now produces 651 CS1591 errors — this is the expected and intended outcome of enabling `GenerateDocumentationFile`. These are pre-existing missing XML doc comments in files not modified by this plan. They are explicitly the subject of plans 02-04 and are out of scope here per the plan's stated objective ("unblock the CS1591-driven documentation work in plans 02 and 03").

## Known Stubs

None.

## Threat Surface Scan

No new network endpoints, auth paths, file access patterns, or schema changes introduced. The access modifier change (`public` → `internal sealed`) reduces the public API surface of `Atomizer.EntityFrameworkCore` — no new threat surface.

## Self-Check

Verified:
- `internal sealed class DatabaseTransactionLeasingScope` present in file
- `DatabaseTransactionLeasingScopeFactory.cs` absent from disk
- `<GenerateDocumentationFile>true</GenerateDocumentationFile>` present in both csproj files inside first PropertyGroup
- Both task commits exist: f8a9431, 66d7839

## Self-Check: PASSED
