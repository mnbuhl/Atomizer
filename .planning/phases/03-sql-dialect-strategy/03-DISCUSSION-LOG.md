# Phase 3: SQL Dialect Strategy - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-05-03
**Phase:** 03-sql-dialect-strategy
**Areas discussed:** Dialect interface scope, RelationalProviderCache fate, Visibility of ISqlDialect, Test coverage in Phase 3

---

## Dialect Interface Scope

| Option | Description | Selected |
|--------|-------------|----------|
| Pre-declare in Phase 3 | ISqlDialect gets all 4 methods now. Dialects stub UpsertSchedule with NotImplementedException. Interface shape stable across phases. | ✓ |
| Keep minimal (3 methods) | Phase 3 covers only what exists today; Phase 4 extends ISqlDialect with UpsertScheduleAsync. | |

**User's choice:** Pre-declare in Phase 3 (Recommended)
**Notes:** Keeps interface shape stable — Phase 4 only implements, no interface extension needed mid-milestone.

---

## RelationalProviderCache Fate

| Option | Description | Selected |
|--------|-------------|----------|
| Keep RelationalProviderCache, expose Dialect property | Cache builds EntityMap + constructs dialect; storage calls `_cache.Dialect`. No DI changes needed. | ✓ |
| Inject ISqlDialect via constructor DI | Register *Dialect as a service; inject into EntityFrameworkCoreStorage. Cleaner DI graph but requires EntityMap at registration time before DbContext is available. | |

**User's choice:** Keep RelationalProviderCache, expose Dialect property
**Notes:** EntityMap building requires IModel from a live DbContext at runtime — injecting ISqlDialect via DI would be awkward.

---

## Visibility of ISqlDialect

| Option | Description | Selected |
|--------|-------------|----------|
| Internal (Recommended) | ISqlDialect and *Dialect classes are internal sealed — consumers add providers via registration extension, not by implementing ISqlDialect. | ✓ |
| Public | ISqlDialect is public — advanced consumers can implement their own dialect. Preserves status quo (IDatabaseProviderSql and providers are currently public). | |

**User's choice:** Internal
**Notes:** Corrects the existing unintentional public visibility; keeps public API surface small.

---

## Test Coverage in Phase 3

| Option | Description | Selected |
|--------|-------------|----------|
| Unit tests in Phase 3 (Recommended) | Each *Dialect gets a unit test asserting generated SQL contains correct keywords. Fast, deterministic, no containers. | ✓ |
| Defer to Phase 4 integration tests | No dialect unit tests — Phase 4 Testcontainer suite proves correctness. Simpler Phase 3. | |

**User's choice:** Unit tests in Phase 3
**Notes:** Catches SQL regressions before the heavier Phase 4 integration pass.

---

## Claude's Discretion

- Exact naming: `ISqlDialect` vs keeping `IDatabaseProviderSql` as the name
- Whether `RelationalProviderCache.Dialect` replaces `RawSqlProvider` in-place or both coexist temporarily with `RawSqlProvider` marked obsolete
- Exact assertion style in dialect unit tests (substring contains vs regex vs exact match)

## Deferred Ideas

None — discussion stayed within phase scope.
