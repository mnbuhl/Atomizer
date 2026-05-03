# Phase 3: SQL Dialect Strategy - Context

**Gathered:** 2026-05-03
**Status:** Ready for planning

<domain>
## Phase Boundary

Rename `IDatabaseProviderSql` → `ISqlDialect` and the three provider classes → `*Dialect`; add `UpsertScheduleAsync` to the interface (stubbed `NotImplementedException` in Phase 3); expose a `Dialect` property on `RelationalProviderCache` so `EntityFrameworkCoreStorage` delegates all raw SQL through `_cache.Dialect` with zero inline `if/switch` on provider type; make `ISqlDialect` and all `*Dialect` classes `internal sealed`; add per-dialect unit tests asserting generated SQL keywords. Phase 3 is EF Core provider layer only — no InMemory changes, no public API changes, no EF Core storage logic changes.

</domain>

<decisions>
## Implementation Decisions

### Dialect Interface Shape

- **D-01:** `ISqlDialect` (replaces `IDatabaseProviderSql`) exposes **4 methods**: `GetDueJobs`, `GetDueSchedules`, `ReleaseLeasedJobs`, and `UpsertScheduleAsync`. All 4 are declared in Phase 3. The first 3 carry real implementations; `UpsertScheduleAsync` stubs `NotImplementedException` in each dialect class, annotated `// TODO: Implemented in Phase 4`.
- **D-02:** Pre-declaring `UpsertScheduleAsync` now keeps the interface shape stable across the milestone — Phase 4 only fills in SQL, no interface extension needed.

### RelationalProviderCache

- **D-03:** `RelationalProviderCache` **survives unchanged** as the internal factory responsible for building `EntityMap` and constructing the correct `*Dialect` instance. It gains a **`public ISqlDialect Dialect` property** (replacing the existing `RawSqlProvider` property). `EntityFrameworkCoreStorage` calls `_cache.Dialect.GetDueJobs(...)` etc. — no direct constructor DI injection of `ISqlDialect`.
- **D-04:** Rationale: `EntityMap` building requires `IModel` from a live `DbContext` at runtime; keeping construction inside `RelationalProviderCache` avoids an awkward DI registration-time dependency.

### Visibility

- **D-05:** `ISqlDialect`, `PostgreSqlDialect`, `SqlServerDialect`, and `MySqlDialect` are all **`internal sealed`** (or `internal` for the interface). This shrinks the public API surface; consumers who need a custom provider will use a registration extension method (not implement `ISqlDialect` directly). The existing `public` visibility on `IDatabaseProviderSql` and `*Provider` classes is corrected here.
- **D-06:** `EntityMap`, `RelationalProviderCache`, and `DatabaseProvider` retain their existing visibility (already internal or effectively so).

### Test Coverage

- **D-07:** Each dialect gets **unit tests** in `tests/Atomizer.EntityFrameworkCore.Tests/` asserting that the generated SQL strings contain the right keywords per provider:
  - `PostgreSqlDialect`: `FOR NO KEY UPDATE SKIP LOCKED`, `LIMIT`
  - `SqlServerDialect`: `WITH (UPDLOCK, READPAST, ROWLOCK)`, `TOP(`
  - `MySqlDialect`: `FOR UPDATE SKIP LOCKED`, `LIMIT`
- **D-08:** Tests instantiate the dialect directly (no DbContext, no containers) using a manually constructed `EntityMap`. Fast, deterministic, no Testcontainers dependency needed.

### Claude's Discretion

- Exact file/class naming: `ISqlDialect` vs keeping `IDatabaseProviderSql` as the name — either is fine; planner picks whichever fits the rename story.
- Whether `RelationalProviderCache.Dialect` replaces `RawSqlProvider` in-place or both exist temporarily with `RawSqlProvider` marked obsolete.
- Exact assertion style in unit tests (contains substring vs regex vs exact match).

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Requirements Governing This Phase
- `.planning/REQUIREMENTS.md` — Requirements DIAL-01, DIAL-02, DIAL-03, DIAL-04 govern Phase 3
- `.planning/ROADMAP.md` — Phase 3 success criteria (4 items) are the acceptance test

### EF Core Provider Files Being Changed
- `src/Atomizer.EntityFrameworkCore/Providers/IDatabaseProviderSql.cs` — Replace with `ISqlDialect`; add `UpsertScheduleAsync` method
- `src/Atomizer.EntityFrameworkCore/Providers/RelationalProviderCache.cs` — Add `Dialect` property; rename internal `RawSqlProvider` usage
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlProvider.cs` — Rename to `PostgreSqlDialect`; implement `ISqlDialect`; stub `UpsertScheduleAsync`
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/SqlServerProvider.cs` — Rename to `SqlServerDialect`; same
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlProvider.cs` — Rename to `MySqlDialect`; same

### EF Core Storage (read only in Phase 3)
- `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` — Update `_providerCache.RawSqlProvider` call sites to `_providerCache.Dialect`; no other changes in Phase 3

### Prior Phase Context (locked decisions)
- `.planning/phases/01-leasing-abstraction/01-CONTEXT.md` — `ExecuteInLeaseAsync` contract shape (still stubbed in EF Core until Phase 4)
- `.planning/phases/02-inmemory-implementation/02-CONTEXT.md` — InMemory is complete and untouched in Phase 3

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `IDatabaseProviderSql` + `PostgreSqlProvider/SqlServerProvider/MySqlProvider` — Phase 3 is a rename + restructure, not a rewrite; all SQL content survives unchanged
- `RelationalProviderCache.CreateRawSqlProvider()` — returns `IDatabaseProviderSql`; update return type to `ISqlDialect` and rename property
- `EntityMap` — already builds the column/table mapping needed by dialect constructors; reuse directly in unit tests by calling `EntityMap.Build(model, type, provider)` with a minimal in-memory EF model

### Established Patterns
- `internal sealed class` — all processing/storage implementations use this; apply to `*Dialect` classes
- Provider `switch` expression in `RelationalProviderCache.CreateRawSqlProvider()` — update to construct `*Dialect` instead of `*Provider`
- `FormattableString` return type on SQL methods — survives unchanged; `EntityFrameworkCoreStorage` uses `FromSqlInterpolated`

### Integration Points
- `EntityFrameworkCoreStorage._providerCache.RawSqlProvider` call sites (3 locations: `GetDueJobsAsync`, `GetDueSchedulesAsync`, `ReleaseLeasedAsync`) → change to `_cache.Dialect`
- No changes to `EntityFrameworkCoreStorage.ExecuteInLeaseAsync` stubs — those are Phase 4

</code_context>

<specifics>
## Specific Ideas

- Dialect unit test pattern — construct a minimal EF Core `ModelBuilder`, apply `modelBuilder.AddAtomizerEntities()`, then call `EntityMap.Build(model, typeof(AtomizerJobEntity), provider)` to get a real `EntityMap` without spinning up a container.
- `ISqlDialect.UpsertScheduleAsync` stub in each dialect:
  ```csharp
  public FormattableString UpsertScheduleAsync(AtomizerSchedule schedule)
  {
      // TODO: Implemented in Phase 4
      throw new NotImplementedException();
  }
  ```

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 3-SQL Dialect Strategy*
*Context gathered: 2026-05-03*
