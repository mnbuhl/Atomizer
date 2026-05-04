---
phase: 09-ef-core-backend-integration-tests
plan: "01"
subsystem: ef-core-entity-layer
tags: [ef-core, entity, fifo, partition-key, sequence-number, sql-dialect]
dependency_graph:
  requires: []
  provides:
    - AtomizerJobEntity.PartitionKey (string?)
    - AtomizerJobEntity.SequenceNumber (long?)
    - AtomizerJobEntityMapper round-trip for PartitionKey and SequenceNumber
    - AtomizerJobEntityConfiguration column config for both new columns
    - ISqlDialect.InsertJobWithSequence method signature
  affects:
    - src/Atomizer.EntityFrameworkCore/Entities/AtomizerJobEntity.cs
    - src/Atomizer.EntityFrameworkCore/Configurations/AtomizerJobEntityConfiguration.cs
    - src/Atomizer.EntityFrameworkCore/Providers/ISqlDialect.cs
tech_stack:
  added: []
  patterns:
    - EF Core nullable column configuration via HasMaxLength(255).IsRequired(false)
    - ISqlDialect strategy interface extension for provider-specific SQL
key_files:
  created: []
  modified:
    - src/Atomizer.EntityFrameworkCore/Entities/AtomizerJobEntity.cs
    - src/Atomizer.EntityFrameworkCore/Configurations/AtomizerJobEntityConfiguration.cs
    - src/Atomizer.EntityFrameworkCore/Providers/ISqlDialect.cs
decisions:
  - ISqlDialect.InsertJobWithSequence added as interface-only contract; dialect implementations deferred to Plan 02 (expected CS0535 build failures are intentional)
  - No explicit HasColumnType on SequenceNumber — EF Core auto-maps long? to bigint on all three supported providers
metrics:
  duration: "2m"
  completed: "2026-05-04"
  tasks_completed: 2
  tasks_total: 2
  files_changed: 3
---

# Phase 09 Plan 01: EF Core Entity Layer FIFO Foundation Summary

EF Core entity, mapper, configuration, and ISqlDialect interface extended with PartitionKey (string?) and SequenceNumber (long?) as the foundational contracts for FIFO processing.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Add PartitionKey and SequenceNumber to AtomizerJobEntity and both mapper directions | 4e792da | src/Atomizer.EntityFrameworkCore/Entities/AtomizerJobEntity.cs |
| 2 | Configure new columns in AtomizerJobEntityConfiguration and add InsertJobWithSequence to ISqlDialect | 5004096 | src/Atomizer.EntityFrameworkCore/Configurations/AtomizerJobEntityConfiguration.cs, src/Atomizer.EntityFrameworkCore/Providers/ISqlDialect.cs |

## What Was Built

### AtomizerJobEntity (Task 1)
- Added `public string? PartitionKey { get; set; }` with XML doc after `IdempotencyKey`
- Added `public long? SequenceNumber { get; set; }` with XML doc before `Errors` navigation property
- `ToEntity` mapper: `PartitionKey = job.PartitionKey?.ToString()` and `SequenceNumber = job.SequenceNumber`
- `ToAtomizerJob` mapper: `PartitionKey = entity.PartitionKey != null ? new PartitionKey(entity.PartitionKey) : null` and `SequenceNumber = entity.SequenceNumber`

### AtomizerJobEntityConfiguration (Task 2)
- `builder.Property(job => job.PartitionKey).HasMaxLength(255).IsRequired(false)` — enforces same 255-char cap as the PartitionKey value object constructor (T-09-01 mitigation)
- `builder.Property(job => job.SequenceNumber).IsRequired(false)` — EF Core auto-maps long? to bigint

### ISqlDialect (Task 2)
- Added `FormattableString InsertJobWithSequence(AtomizerJob job)` with XML doc
- Dialect implementations (PostgreSqlDialect, SqlServerDialect, MySqlDialect) will implement this in Plan 02

## Deviations from Plan

None — plan executed exactly as written. The expected CS0535 build failures on all three dialect classes are confirmed and correct per the plan's verification section.

## Verification Results

All grep checks pass:
- `grep -c "public string? PartitionKey" AtomizerJobEntity.cs` → 1
- `grep -c "public long? SequenceNumber" AtomizerJobEntity.cs` → 1
- `grep -c "InsertJobWithSequence" ISqlDialect.cs` → 1
- `grep -c "HasMaxLength(255)" AtomizerJobEntityConfiguration.cs` → 1

Build status: Expected CS0535 failures on MySqlDialect, PostgreSqlDialect, SqlServerDialect (dialect implementations deferred to Plan 02).

## Known Stubs

None — this plan establishes contracts only; no stub data flows to UI rendering.

## Threat Surface Scan

No new network endpoints, auth paths, file access patterns, or schema changes at trust boundaries beyond what the plan's threat model covers.

## Self-Check: PASSED

- 4e792da: feat(09-01): add PartitionKey and SequenceNumber to AtomizerJobEntity — FOUND
- 5004096: feat(09-01): configure PartitionKey/SequenceNumber columns and add InsertJobWithSequence to ISqlDialect — FOUND
- src/Atomizer.EntityFrameworkCore/Entities/AtomizerJobEntity.cs — exists, contains `public string? PartitionKey`
- src/Atomizer.EntityFrameworkCore/Configurations/AtomizerJobEntityConfiguration.cs — exists, contains `HasMaxLength(255)`
- src/Atomizer.EntityFrameworkCore/Providers/ISqlDialect.cs — exists, contains `InsertJobWithSequence`
