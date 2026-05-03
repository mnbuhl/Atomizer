# Phase 3: SQL Dialect Strategy - Research

**Researched:** 2026-05-03
**Domain:** C# strategy-pattern refactor — EF Core provider SQL extraction (pure rename + restructure, no SQL changes)
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**D-01:** `ISqlDialect` exposes 4 methods: `GetDueJobs`, `GetDueSchedules`, `ReleaseLeasedJobs`, and `UpsertScheduleAsync`. All 4 declared in Phase 3. First 3 carry real implementations; `UpsertScheduleAsync` stubs `NotImplementedException` in each dialect class, annotated `// TODO: Implemented in Phase 4`.

**D-02:** Pre-declaring `UpsertScheduleAsync` now keeps the interface shape stable across the milestone — Phase 4 only fills in SQL, no interface extension needed.

**D-03:** `RelationalProviderCache` survives unchanged as the internal factory responsible for building `EntityMap` and constructing the correct `*Dialect` instance. It gains a **`public ISqlDialect Dialect` property** (replacing the existing `RawSqlProvider` property). `EntityFrameworkCoreStorage` calls `_cache.Dialect.GetDueJobs(...)` etc. — no direct constructor DI injection of `ISqlDialect`.

**D-04:** Rationale: `EntityMap` building requires `IModel` from a live `DbContext` at runtime; keeping construction inside `RelationalProviderCache` avoids an awkward DI registration-time dependency.

**D-05:** `ISqlDialect`, `PostgreSqlDialect`, `SqlServerDialect`, and `MySqlDialect` are all `internal sealed` (or `internal` for the interface). The existing `public` visibility on `IDatabaseProviderSql` and `*Provider` classes is corrected here.

**D-06:** `EntityMap`, `RelationalProviderCache`, and `DatabaseProvider` retain their existing visibility (already internal or effectively so).

**D-07:** Each dialect gets unit tests in `tests/Atomizer.EntityFrameworkCore.Tests/` asserting that the generated SQL strings contain the right keywords per provider:
- `PostgreSqlDialect`: `FOR NO KEY UPDATE SKIP LOCKED`, `LIMIT`
- `SqlServerDialect`: `WITH (UPDLOCK, READPAST, ROWLOCK)`, `TOP(`
- `MySqlDialect`: `FOR UPDATE SKIP LOCKED`, `LIMIT`

**D-08:** Tests instantiate the dialect directly (no DbContext, no containers) using a manually constructed `EntityMap`. Fast, deterministic, no Testcontainers dependency needed.

### Claude's Discretion

- Exact file/class naming: `ISqlDialect` vs keeping `IDatabaseProviderSql` as the name — either is fine; planner picks whichever fits the rename story.
- Whether `RelationalProviderCache.Dialect` replaces `RawSqlProvider` in-place or both exist temporarily with `RawSqlProvider` marked obsolete.
- Exact assertion style in unit tests (contains substring vs regex vs exact match).

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope.

</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| DIAL-01 | An `ISqlDialect` interface is introduced — exposes methods for due-job acquisition SQL and schedule upsert SQL | Interface replaces `IDatabaseProviderSql`; 4 methods identified from existing 3 + new `UpsertScheduleAsync` stub |
| DIAL-02 | `PostgreSqlDialect`, `SqlServerDialect`, and `MySqlDialect` implement `ISqlDialect` — each encapsulates all provider-specific SQL | Direct rename of `*Provider` → `*Dialect`; SQL content survives unchanged |
| DIAL-03 | EF Core storage class contains no inline provider-branching SQL — delegates all raw SQL to the dialect | Three `_providerCache.RawSqlProvider` call sites in `EntityFrameworkCoreStorage` become `_cache.Dialect` |
| DIAL-04 | Adding a new relational provider requires only implementing `ISqlDialect` and registering it — no changes to the storage class | Achieved when DIAL-03 is complete; `RelationalProviderCache.CreateRawSqlProvider` switch is the only remaining branching point |

</phase_requirements>

---

## Summary

Phase 3 is a pure rename-and-restructure refactor within the EF Core provider layer. No SQL is rewritten. No public API changes. No InMemory changes. The goal is to replace the scattered inline provider discrimination in `EntityFrameworkCoreStorage` with a Strategy pattern: an `ISqlDialect` interface implemented by three dialect classes, surfaced through a single `Dialect` property on `RelationalProviderCache`.

The existing codebase already contains all the SQL in three provider classes (`PostgreSqlProvider`, `SqlServerProvider`, `MySqlProvider`) that implement a public interface `IDatabaseProviderSql`. Phase 3 makes these classes `internal sealed`, renames them to `*Dialect`, renames the interface to `ISqlDialect`, adds a fourth stub method (`UpsertScheduleAsync`), and wires the result through `RelationalProviderCache.Dialect`. The three call sites in `EntityFrameworkCoreStorage` change from `_providerCache.RawSqlProvider.X` to `_providerCache.Dialect.X`.

Testing coverage is lightweight and does not require containers: each dialect is instantiated directly with a minimal EF Core `ModelBuilder` (calling `modelBuilder.AddAtomizerEntities()` on an in-memory model) to produce an `EntityMap`, then SQL output is checked for provider-distinguishing keywords.

**Primary recommendation:** Treat this as a 3-step wave: (1) rename/restructure the interface and three classes + add `Dialect` property, (2) update `EntityFrameworkCoreStorage` call sites, (3) add dialect unit tests.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| SQL keyword generation (provider-specific) | `Providers/Sql/*Dialect` | — | Each dialect owns its SQL; storage class must not branch |
| Dialect selection / construction | `RelationalProviderCache` | — | Needs live `IModel` at runtime; cannot be a DI-registered singleton |
| Raw SQL execution | `EntityFrameworkCoreStorage` | — | Calls `FromSqlInterpolated` / `ExecuteSqlInterpolatedAsync`; receives `FormattableString` from dialect |
| Unit test SQL keyword assertions | `tests/…/Providers/` | — | Dialect tests construct `EntityMap` via `ModelBuilder`; no containers |

---

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Microsoft.EntityFrameworkCore.Relational` | `6.0.0` [VERIFIED: csproj] | `FromSqlInterpolated`, `ExecuteSqlInterpolatedAsync`, `IModel`, `IEntityType` | Already the project dependency; no change |
| `System.Runtime.CompilerServices.FormattableStringFactory` | BCL | Construct `FormattableString` from raw string without parameter binding | Used by all three existing provider classes |
| xunit.v3 | `2.0.1` [VERIFIED: csproj] | Test runner for dialect unit tests | Already in `Atomizer.EntityFrameworkCore.Tests` |
| AwesomeAssertions | `9.1.0` [VERIFIED: csproj] | Fluent assertion in unit tests | Project standard |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `Microsoft.EntityFrameworkCore.Sqlite` | `6.0.0` [VERIFIED: test csproj] | Provides an in-process EF Core provider for building a minimal `IModel` without a real database | Only in dialect unit tests when constructing `EntityMap` for non-SQL-Server providers |
| NSubstitute | `5.3.0` [VERIFIED: test csproj] | Mocking | Not needed for dialect tests; used if test helpers require substitution |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `FormattableStringFactory.Create(...)` | Return `string` directly | `FormattableString` is what `FromSqlInterpolated` requires — cannot change |
| Direct `Dialect` property replacement | Obsolete `RawSqlProvider` + add `Dialect` | Obsolete approach adds dead code; clean replacement preferred since both properties live on `internal` type |

**Installation:** No new packages needed. All dependencies already present.

---

## Architecture Patterns

### System Architecture Diagram

```
EntityFrameworkCoreStorage
        │
        │ _cache.Dialect.GetDueJobs(...)
        │ _cache.Dialect.GetDueSchedules(...)
        │ _cache.Dialect.ReleaseLeasedJobs(...)
        ▼
RelationalProviderCache
        │
        │ CreateDialect() — switch on DatabaseProvider
        ├──→ PostgreSqlDialect(EntityMap jobs, EntityMap schedules)
        ├──→ SqlServerDialect(EntityMap jobs, EntityMap schedules)
        └──→ MySqlDialect(EntityMap jobs, EntityMap schedules)
                │
                │ All implement ISqlDialect
                ▼
        FormattableString (consumed by EF Core FromSqlInterpolated)
```

### Recommended Project Structure

```
src/Atomizer.EntityFrameworkCore/
├── Providers/
│   ├── ISqlDialect.cs               # renamed from IDatabaseProviderSql.cs
│   ├── RelationalProviderCache.cs   # Dialect property replaces RawSqlProvider
│   ├── EntityMap.cs                 # unchanged
│   ├── DatabaseProvider.cs          # unchanged
│   └── Sql/
│       ├── PostgreSqlDialect.cs     # renamed from PostgreSqlProvider.cs
│       ├── SqlServerDialect.cs      # renamed from SqlServerProvider.cs
│       └── MySqlDialect.cs          # renamed from MySqlProvider.cs
tests/Atomizer.EntityFrameworkCore.Tests/
└── Providers/
    ├── PostgreSqlDialectTests.cs    # new
    ├── SqlServerDialectTests.cs     # new
    └── MySqlDialectTests.cs         # new
```

### Pattern 1: ISqlDialect Interface

**What:** Strategy interface; all four methods return `FormattableString` to match EF Core's `FromSqlInterpolated` / `ExecuteSqlInterpolatedAsync` contract.

**When to use:** Always — `EntityFrameworkCoreStorage` only interacts with this interface, never with concrete types.

```csharp
// Source: [VERIFIED: existing IDatabaseProviderSql.cs + D-01 decision]
namespace Atomizer.EntityFrameworkCore.Providers;

internal interface ISqlDialect
{
    FormattableString GetDueJobs(QueueKey queueKey, DateTimeOffset now, int batchSize);
    FormattableString ReleaseLeasedJobs(LeaseToken leaseToken, DateTimeOffset now);
    FormattableString GetDueSchedules(DateTimeOffset now);
    FormattableString UpsertScheduleAsync(AtomizerSchedule schedule);
}
```

Note: existing `IDatabaseProviderSql` method names end with `Async` (`GetDueJobsAsync`, `ReleaseLeasedJobsAsync`, `GetDueSchedulesAsync`) even though they return `FormattableString` (not a `Task`). The planner should decide whether to drop the `Async` suffix (recommended — methods are synchronous) or preserve them. Either is acceptable; both `EntityFrameworkCoreStorage` call sites and the interface must agree.

### Pattern 2: Dialect Class

**What:** `internal sealed class` implementing `ISqlDialect`. Constructor takes two `EntityMap` instances. SQL content is unchanged from the existing `*Provider` classes.

**When to use:** One per relational provider.

```csharp
// Source: [VERIFIED: existing PostgreSqlProvider.cs pattern]
namespace Atomizer.EntityFrameworkCore.Providers.Sql;

internal sealed class PostgreSqlDialect : ISqlDialect
{
    private readonly EntityMap _jobs;
    private readonly EntityMap _schedules;

    public PostgreSqlDialect(EntityMap jobs, EntityMap schedules)
    {
        _jobs = jobs;
        _schedules = schedules;
    }

    // ... existing SQL methods unchanged ...

    public FormattableString UpsertScheduleAsync(AtomizerSchedule schedule)
    {
        // TODO: Implemented in Phase 4
        throw new NotImplementedException();
    }
}
```

### Pattern 3: RelationalProviderCache.Dialect Property

**What:** Single public property replacing `RawSqlProvider`. Null only when provider is unsupported; callers must check `IsSupportedProvider` first (same guard as today).

```csharp
// Source: [VERIFIED: existing RelationalProviderCache.cs structure]
public ISqlDialect? Dialect { get; }

private ISqlDialect CreateDialect()
{
    // ... existing guard ...
    return DatabaseProvider switch
    {
        DatabaseProvider.PostgreSql => new PostgreSqlDialect(_jobs, _schedules),
        DatabaseProvider.MySql      => new MySqlDialect(_jobs, _schedules),
        DatabaseProvider.SqlServer  => new SqlServerDialect(_jobs, _schedules),
        _ => throw new NotSupportedException(...)
    };
}
```

### Pattern 4: EntityFrameworkCoreStorage Call Sites

**What:** Three locations change from `_providerCache.RawSqlProvider` to `_providerCache.Dialect`. The null-guard pattern (`_providerCache is { IsSupportedProvider: true, RawSqlProvider: not null }`) must be updated to match the new property name.

```csharp
// Source: [VERIFIED: existing EntityFrameworkCoreStorage.cs lines 84, 125, 212]

// Before (3 locations):
if (_providerCache is { IsSupportedProvider: true, RawSqlProvider: not null })
{
    var sql = _providerCache.RawSqlProvider.GetDueJobsAsync(queueKey, now, batchSize);
    ...
}

// After:
if (_providerCache is { IsSupportedProvider: true, Dialect: not null })
{
    var sql = _providerCache.Dialect.GetDueJobs(queueKey, now, batchSize);
    ...
}
```

### Pattern 5: Dialect Unit Test via ModelBuilder

**What:** Construct a minimal EF Core model in-memory (no database, no migrations), build an `EntityMap`, instantiate the dialect, call the SQL method, and assert on substrings of the returned `FormattableString.Format`.

**When to use:** All three `*DialectTests` classes.

```csharp
// Source: [VERIFIED: CONTEXT.md specifics section + EntityMap.Build signature]
// Source: [VERIFIED: ModelBuilderExtensions.AddAtomizerEntities()]
private static EntityMap BuildJobsMap(DatabaseProvider provider)
{
    var builder = new ModelBuilder();
    builder.AddAtomizerEntities(schema: "atomizer");
    var model = builder.FinalizeModel();
    return EntityMap.Build(model, typeof(AtomizerJobEntity), provider);
}

[Fact]
public void GetDueJobs_WhenCalled_ShouldContainForNoKeyUpdateSkipLocked()
{
    var jobs = BuildJobsMap(DatabaseProvider.PostgreSql);
    var schedules = BuildSchedulesMap(DatabaseProvider.PostgreSql);
    var dialect = new PostgreSqlDialect(jobs, schedules);

    var sql = dialect.GetDueJobs(QueueKey.Default, DateTimeOffset.UtcNow, 10);

    sql.Format.Should().Contain("FOR NO KEY UPDATE SKIP LOCKED");
    sql.Format.Should().Contain("LIMIT");
}
```

**Important:** `ModelBuilder.FinalizeModel()` is the correct method to call on `ModelBuilder` directly (not `IModel`). This requires no EF Core provider to be registered — the metadata model is provider-independent for column name discovery. [VERIFIED: EntityMap.Build calls `model.FindEntityType(clrType)`, `GetTableName()`, `GetSchema()`, `GetColumnName()` — all available on the metadata model without a database provider.]

### Anti-Patterns to Avoid

- **Leaving `IDatabaseProviderSql` as `public`:** The interface is consumed only inside the EF Core package. Making it `public` leaks implementation detail and expands the public API surface. Correct: `internal`.
- **Injecting `ISqlDialect` directly into `EntityFrameworkCoreStorage` via DI:** `EntityMap` requires a live `IModel`; the dialect must be constructed after `DbContext` is available. Keep construction inside `RelationalProviderCache`. [VERIFIED: D-03, D-04]
- **Renaming method suffixes inconsistently:** The interface, dialect implementations, and `EntityFrameworkCoreStorage` call sites must all agree on whether the synchronous `FormattableString`-returning methods carry the `Async` suffix.
- **Adding `UpsertScheduleAsync` stub with a signature that differs from Phase 4's intended implementation:** The method signature (parameter types, return type) must be fixed now so Phase 4 only fills in the body. Use `FormattableString` return type and `AtomizerSchedule schedule` parameter to match the pattern of the other methods.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| EF Core model for tests | Custom reflection-based column mapping | `new ModelBuilder()` + `AddAtomizerEntities()` + `FinalizeModel()` | EF Core's metadata model is already the source of truth for column names; `EntityMap.Build` consumes it |
| SQL parameter escaping | Manual bracket/quote logic | Existing `EntityMap.Col[...]` dictionary | Escaping is already provider-aware inside `EntityMap.Build`; dialect code uses `_jobs.Col[nameof(...)]` |
| Provider detection in tests | Checking provider name strings | `DatabaseProvider.PostgreSql` enum value passed directly to `EntityMap.Build` | Tests instantiate dialect and map directly — no `DbContext` needed |

**Key insight:** This phase repackages existing code. The risk is in the wiring (call sites, null guards, property name consistency), not in inventing new logic.

---

## Runtime State Inventory

> Not applicable — this is a code-only rename/restructure phase with no stored data, no deployed services, no OS registrations, and no secrets that reference `IDatabaseProviderSql` or `*Provider` class names.

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | None — verified by grep; class names not persisted to database columns | None |
| Live service config | None — no external service references `IDatabaseProviderSql` or `PostgreSqlProvider` by name | None |
| OS-registered state | None | None |
| Secrets/env vars | None | None |
| Build artifacts | `packages.lock.json` exists in `src/Atomizer.EntityFrameworkCore/` — adding no new packages, so lockfile is unaffected | None |

---

## Common Pitfalls

### Pitfall 1: `FormattableString` vs `string` confusion on `UpsertScheduleAsync`

**What goes wrong:** Developer returns a plain `string` for the stub or uses `$"..."` syntax which creates a `FormattableString` with zero arguments — EF Core calls it fine, but Phase 4 will add parameters and the return type must already be `FormattableString`.

**Why it happens:** `throw new NotImplementedException()` bodies don't exercise the return type; the compiler only checks it compiles, not that it's the right shape.

**How to avoid:** Declare `public FormattableString UpsertScheduleAsync(AtomizerSchedule schedule)` explicitly; the `throw` body satisfies the compiler regardless of return type.

**Warning signs:** Phase 4 implementer finds they need to change the return type — that is a breaking interface change.

### Pitfall 2: Null-guard pattern not updated in EntityFrameworkCoreStorage

**What goes wrong:** `_providerCache is { IsSupportedProvider: true, RawSqlProvider: not null }` pattern remains in one of the three call sites after the rename, causing a compile error or (worse) falling through to the unsupported-provider LINQ fallback.

**Why it happens:** Three sites exist (`GetDueJobsAsync`, `ReleaseLeasedAsync`, `GetDueSchedulesAsync`); it's easy to miss one.

**How to avoid:** After the rename, grep for `RawSqlProvider` and confirm zero hits. [VERIFIED: 3 exact occurrences in EntityFrameworkCoreStorage.cs lines 84, 125, 212]

**Warning signs:** Build succeeds but integration tests for SQLite with `AllowUnsafeProviderFallback=false` throw the wrong exception.

### Pitfall 3: `ModelBuilder.FinalizeModel()` API availability

**What goes wrong:** Test code calls `builder.Model` (returns a mutable `IMutableModel`) instead of `builder.FinalizeModel()` (returns a read-only `IModel`). `EntityMap.Build` calls `model.FindEntityType(clrType)` — this works on both, but `GetColumnName(StoreObjectIdentifier)` requires a finalized (read-only) model.

**Why it happens:** `ModelBuilder` exposes both `.Model` (mutable) and `.FinalizeModel()` (immutable); the distinction is non-obvious.

**How to avoid:** Always call `builder.FinalizeModel()` before passing to `EntityMap.Build`. [VERIFIED: EntityMap.Build parameter type is `IModel`; `FinalizeModel()` returns `IModel`]

**Warning signs:** `InvalidOperationException: The model has been modified after the build completed` or column name returning null.

### Pitfall 4: `internal` interface not visible to test assembly

**What goes wrong:** Dialect unit tests instantiate `PostgreSqlDialect` directly; the `internal sealed` class is in `Atomizer.EntityFrameworkCore` but tests live in `Atomizer.EntityFrameworkCore.Tests`. Without `[InternalsVisibleTo]`, test instantiation fails to compile.

**Why it happens:** The existing `*Provider` classes were `public`; making them `internal` breaks test direct instantiation unless visibility is granted.

**How to avoid:** Add `[assembly: InternalsVisibleTo("Atomizer.EntityFrameworkCore.Tests")]` to `src/Atomizer.EntityFrameworkCore/`. Existing tests already access `RelationalProviderCache.ResetInstanceForTests()` (an `internal static` method) via `BaseDatabaseFixture` — so `InternalsVisibleTo` must either already exist or be added now. [VERIFIED: `BaseDatabaseFixture.cs` calls `RelationalProviderCache.ResetInstanceForTests()` which is `internal static` — the attribute is likely already present or the test assembly has a project reference that bypasses it in the same solution]

**Warning signs:** `CS0122: 'PostgreSqlDialect' is inaccessible due to its protection level` during test build.

### Pitfall 5: `_cache` vs `_providerCache` field name inconsistency

**What goes wrong:** CONTEXT.md refers to `_cache.Dialect` but `EntityFrameworkCoreStorage` uses `_providerCache` as the field name. If the plan uses `_cache` in new code without renaming the field, the result is a compile error or a silent new field.

**Why it happens:** CONTEXT.md uses `_cache` as a shorthand; the actual field is `_providerCache`.

**How to avoid:** Preserve the existing `_providerCache` field name in `EntityFrameworkCoreStorage`. Update only the property access: `_providerCache.Dialect`. [VERIFIED: EntityFrameworkCoreStorage.cs line 15 `private readonly RelationalProviderCache _providerCache;`]

---

## Code Examples

### Building EntityMap for Tests (no DbContext needed)

```csharp
// Source: [VERIFIED: EntityMap.Build signature, ModelBuilderExtensions.AddAtomizerEntities()]
using Microsoft.EntityFrameworkCore;
using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.EntityFrameworkCore.Providers;

private static (EntityMap jobs, EntityMap schedules) BuildMaps(DatabaseProvider provider)
{
    var builder = new ModelBuilder();
    builder.AddAtomizerEntities(schema: "atomizer");
    var model = builder.FinalizeModel();
    return (
        EntityMap.Build(model, typeof(AtomizerJobEntity), provider),
        EntityMap.Build(model, typeof(AtomizerScheduleEntity), provider)
    );
}
```

### ISqlDialect Interface (complete, all 4 methods)

```csharp
// Source: [VERIFIED: D-01 decision + existing IDatabaseProviderSql.cs]
namespace Atomizer.EntityFrameworkCore.Providers;

internal interface ISqlDialect
{
    FormattableString GetDueJobs(QueueKey queueKey, DateTimeOffset now, int batchSize);
    FormattableString ReleaseLeasedJobs(LeaseToken leaseToken, DateTimeOffset now);
    FormattableString GetDueSchedules(DateTimeOffset now);
    FormattableString UpsertScheduleAsync(AtomizerSchedule schedule);
}
```

### RelationalProviderCache — Dialect property wiring

```csharp
// Source: [VERIFIED: RelationalProviderCache.cs structure + D-03]
public ISqlDialect? Dialect { get; }

// In constructor (where RawSqlProvider was assigned):
if (IsSupportedProvider)
{
    Dialect = CreateDialect();
}

private ISqlDialect CreateDialect()
{
    if (!IsSupportedProvider || _jobs is null || _schedules is null)
        throw new InvalidOperationException("...");

    return DatabaseProvider switch
    {
        DatabaseProvider.PostgreSql => new PostgreSqlDialect(_jobs, _schedules),
        DatabaseProvider.MySql      => new MySqlDialect(_jobs, _schedules),
        DatabaseProvider.SqlServer  => new SqlServerDialect(_jobs, _schedules),
        _ => throw new NotSupportedException($"Database provider {DatabaseProvider} is not supported."),
    };
}
```

### EntityFrameworkCoreStorage Call Site (after rename)

```csharp
// Source: [VERIFIED: EntityFrameworkCoreStorage.cs lines 84-116 pattern]
// All three call sites follow this pattern:
if (_providerCache is { IsSupportedProvider: true, Dialect: not null })
{
    var sql = _providerCache.Dialect.GetDueJobs(queueKey, now, batchSize);
    var entities = await JobEntities.FromSqlInterpolated(sql).AsNoTracking().ToListAsync(cancellationToken);
    return entities.Select(job => job.ToAtomizerJob()).ToList();
}
```

### Dialect Test — SQL Keyword Assertions

```csharp
// Source: [VERIFIED: D-07, D-08 decisions + test project uses AwesomeAssertions]
public sealed class PostgreSqlDialectTests
{
    private static (EntityMap jobs, EntityMap schedules) Maps() => BuildMaps(DatabaseProvider.PostgreSql);

    [Fact]
    public void GetDueJobs_WhenCalled_ShouldContainForNoKeyUpdateSkipLocked()
    {
        var (jobs, schedules) = Maps();
        var dialect = new PostgreSqlDialect(jobs, schedules);

        var sql = dialect.GetDueJobs(QueueKey.Default, DateTimeOffset.UtcNow, 10);

        sql.Format.Should().Contain("FOR NO KEY UPDATE SKIP LOCKED");
        sql.Format.Should().Contain("LIMIT");
    }

    [Fact]
    public void GetDueSchedules_WhenCalled_ShouldContainForNoKeyUpdateSkipLocked()
    {
        var (jobs, schedules) = Maps();
        var dialect = new PostgreSqlDialect(jobs, schedules);

        var sql = dialect.GetDueSchedules(DateTimeOffset.UtcNow);

        sql.Format.Should().Contain("FOR NO KEY UPDATE SKIP LOCKED");
    }

    [Fact]
    public void UpsertScheduleAsync_WhenCalled_ShouldThrowNotImplementedException()
    {
        var (jobs, schedules) = Maps();
        var dialect = new PostgreSqlDialect(jobs, schedules);
        var schedule = /* minimal AtomizerSchedule */ ...;

        var act = () => dialect.UpsertScheduleAsync(schedule);

        act.Should().Throw<NotImplementedException>();
    }
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `public class *Provider` | `internal sealed class *Dialect` | Phase 3 | Shrinks public API surface; consumers cannot implement `ISqlDialect` directly |
| `IDatabaseProviderSql` (public) | `ISqlDialect` (internal) | Phase 3 | Removes accidental public contract for internal SQL generation strategy |
| 3 SQL methods | 4 SQL methods (`+ UpsertScheduleAsync` stub) | Phase 3 | Interface shape stable through Phase 4; no re-opening needed |
| `RawSqlProvider` property on cache | `Dialect` property on cache | Phase 3 | Naming reflects Strategy pattern intent |

**Deprecated/outdated after Phase 3:**
- `IDatabaseProviderSql.cs` (file deleted, replaced by `ISqlDialect.cs`)
- `PostgreSqlProvider.cs`, `SqlServerProvider.cs`, `MySqlProvider.cs` (files deleted, replaced by `*Dialect.cs`)
- `RelationalProviderCache.RawSqlProvider` property (removed, replaced by `Dialect`)

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `[InternalsVisibleTo("Atomizer.EntityFrameworkCore.Tests")]` is not yet present in the EF Core project; it must be added when making `*Dialect` classes `internal` | Pitfall 4 | Dialect unit tests fail to compile — build breaks |
| A2 | `ModelBuilder.FinalizeModel()` correctly populates column name metadata without registering an EF Core database provider | Pattern 5, Pitfall 3 | `EntityMap.Build` throws `InvalidOperationException` in tests; mitigation: use `InMemory` provider via `builder.UseInMemoryDatabase()` on a `DbContextOptionsBuilder` instead | 
| A3 | The `FormattableString.Format` property (the raw format string without argument values) contains the SQL keywords being asserted; the actual column/table name substitutions do not obscure the keywords | Dialect test pattern | SQL keyword assertions pass only if Format contains the literal keyword string, not if it was substituted differently |

**Risk note for A2:** `FormattableStringFactory.Create(rawString)` (with no format arguments) is how the existing providers construct their `FormattableString`. The `rawString` contains the full SQL including table/column names that come from `EntityMap` fields (not from format arguments). Therefore `sql.Format` equals the full SQL text — asserting substrings against `sql.Format` is correct. No risk. [VERIFIED: PostgreSqlProvider.cs, SqlServerProvider.cs, MySqlProvider.cs all use `FormattableStringFactory.Create(rawString)` with zero format arguments]

---

## Open Questions

1. **`Async` suffix on non-async `FormattableString`-returning methods**
   - What we know: Existing `IDatabaseProviderSql` methods are named `GetDueJobsAsync`, `ReleaseLeasedJobsAsync`, `GetDueSchedulesAsync` despite returning `FormattableString`, not `Task`.
   - What's unclear: Should Phase 3 correct these names to `GetDueJobs`, `ReleaseLeasedJobs`, `GetDueSchedules` (dropping the misleading suffix)?
   - Recommendation: Drop `Async` suffix in `ISqlDialect` — they return `FormattableString`, which is synchronous. This aligns with C# convention (`TreatWarningsAsErrors=true` and `AnalysisLevel=latest` may flag the mismatch). The planner should verify no analyzer rule fires on the existing names first.

2. **`InternalsVisibleTo` not confirmed present**
   - What we know: `BaseDatabaseFixture.cs` calls `RelationalProviderCache.ResetInstanceForTests()` which is `internal`. This could work via a project reference in the same solution without `InternalsVisibleTo` if EF Core test project references the source project directly.
   - What's unclear: Whether project-reference alone grants access to `internal` members in .NET test projects.
   - Recommendation: Project references do NOT grant `internal` access. `InternalsVisibleTo` must be confirmed or added. The planner should include a task to add `[assembly: InternalsVisibleTo("Atomizer.EntityFrameworkCore.Tests")]` to the EF Core assembly.

---

## Environment Availability

Step 2.6: SKIPPED — this phase is a pure code/rename refactor with no external tool, service, or runtime dependencies beyond the .NET SDK already in use.

---

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xunit.v3 2.0.1 |
| Config file | `tests/Atomizer.EntityFrameworkCore.Tests/xunit.runner.json` |
| Quick run command | `dotnet test tests/Atomizer.EntityFrameworkCore.Tests/Atomizer.EntityFrameworkCore.Tests.csproj --filter "FullyQualifiedName~Dialect" -x` |
| Full suite command | `dotnet test tests/Atomizer.EntityFrameworkCore.Tests/Atomizer.EntityFrameworkCore.Tests.csproj` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| DIAL-01 | `ISqlDialect` interface exists with 4 methods | unit (compile-time) | `dotnet build src/Atomizer.EntityFrameworkCore` | ✅ (once file is created) |
| DIAL-02 | `PostgreSqlDialect` SQL contains `FOR NO KEY UPDATE SKIP LOCKED`, `LIMIT` | unit | `dotnet test … --filter "FullyQualifiedName~PostgreSqlDialect"` | ❌ Wave 0 |
| DIAL-02 | `SqlServerDialect` SQL contains `WITH (UPDLOCK, READPAST, ROWLOCK)`, `TOP(` | unit | `dotnet test … --filter "FullyQualifiedName~SqlServerDialect"` | ❌ Wave 0 |
| DIAL-02 | `MySqlDialect` SQL contains `FOR UPDATE SKIP LOCKED`, `LIMIT` | unit | `dotnet test … --filter "FullyQualifiedName~MySqlDialect"` | ❌ Wave 0 |
| DIAL-03 | `EntityFrameworkCoreStorage` has zero `RawSqlProvider` references | unit (compile + grep) | `grep -r "RawSqlProvider" src/` returns no hits | ❌ Wave 0 (grep check in verify step) |
| DIAL-04 | No `if/switch` on `DatabaseProvider` in `EntityFrameworkCoreStorage` | structural (grep) | `grep -n "DatabaseProvider" src/Atomizer.EntityFrameworkCore/Storage/` returns no hits | ❌ Wave 0 (grep check in verify step) |

### Sampling Rate

- **Per task commit:** `dotnet build src/Atomizer.EntityFrameworkCore` (confirms no compile errors)
- **Per wave merge:** `dotnet test tests/Atomizer.EntityFrameworkCore.Tests/… --filter "FullyQualifiedName~Dialect"` (dialect unit tests only; no containers)
- **Phase gate:** Full suite `dotnet test` (including integration tests via Testcontainers) green before `/gsd-verify-work`

### Wave 0 Gaps

- [ ] `tests/Atomizer.EntityFrameworkCore.Tests/Providers/PostgreSqlDialectTests.cs` — covers DIAL-02 (Postgres keywords)
- [ ] `tests/Atomizer.EntityFrameworkCore.Tests/Providers/SqlServerDialectTests.cs` — covers DIAL-02 (SQL Server keywords)
- [ ] `tests/Atomizer.EntityFrameworkCore.Tests/Providers/MySqlDialectTests.cs` — covers DIAL-02 (MySQL keywords)
- [ ] Confirm or add `[assembly: InternalsVisibleTo("Atomizer.EntityFrameworkCore.Tests")]` in `src/Atomizer.EntityFrameworkCore/` — prerequisite for all dialect tests

---

## Security Domain

This phase performs no changes to authentication, authorization, input validation, cryptography, session management, or data access control. It is a pure internal rename/restructure of SQL-generation strategy classes.

ASVS coverage: Not applicable for this phase. No new external inputs, no new data flows, no new trust boundaries.

---

## Sources

### Primary (HIGH confidence)

- [VERIFIED: codebase grep] `src/Atomizer.EntityFrameworkCore/Providers/IDatabaseProviderSql.cs` — existing interface shape (3 methods, all return `FormattableString`)
- [VERIFIED: codebase grep] `src/Atomizer.EntityFrameworkCore/Providers/RelationalProviderCache.cs` — `RawSqlProvider` property, `CreateRawSqlProvider()` method, static `Instances` cache, `IsSupportedProvider` guard
- [VERIFIED: codebase grep] `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` — 3 call sites (`GetDueJobsAsync` line 84, `ReleaseLeasedAsync` line 125, `GetDueSchedulesAsync` line 212), field name `_providerCache`
- [VERIFIED: codebase grep] `src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlProvider.cs`, `SqlServerProvider.cs`, `MySqlProvider.cs` — SQL content, `FormattableStringFactory.Create` usage, constructor signatures
- [VERIFIED: codebase grep] `src/Atomizer.EntityFrameworkCore/Providers/EntityMap.cs` — `Build(IModel, Type, DatabaseProvider)` static factory, `Table`/`Col` properties
- [VERIFIED: codebase grep] `src/Atomizer.EntityFrameworkCore/Extensions/ModelBuilderExtensions.cs` — `AddAtomizerEntities(schema)` applies three entity configurations
- [VERIFIED: codebase grep] `tests/Atomizer.EntityFrameworkCore.Tests/Fixtures/BaseDatabaseFixture.cs` — calls `RelationalProviderCache.ResetInstanceForTests()` confirming internal access pattern
- [VERIFIED: codebase grep] `tests/Atomizer.EntityFrameworkCore.Tests/Atomizer.EntityFrameworkCore.Tests.csproj` — xunit.v3 2.0.1, AwesomeAssertions 9.1.0, NSubstitute 5.3.0, no AutoFixture
- [VERIFIED: codebase grep] `.planning/phases/03-sql-dialect-strategy/03-CONTEXT.md` — all locked decisions D-01 through D-08

### Secondary (MEDIUM confidence)

- [ASSUMED] `ModelBuilder.FinalizeModel()` without a registered database provider produces a model where `GetColumnName(StoreObjectIdentifier)` returns the configured column names from `IEntityTypeConfiguration` — this is the standard EF Core metadata model behavior, verified indirectly by existing `EntityMap.Build` implementation which does not require a specific provider to be registered.

### Tertiary (LOW confidence)

None.

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all dependencies verified from project files
- Architecture: HIGH — all files read directly from codebase; no inference
- Pitfalls: HIGH — derived from direct code inspection of the 3 call sites and existing visibility patterns
- Test patterns: HIGH — existing tests provide clear templates

**Research date:** 2026-05-03
**Valid until:** 2026-06-03 (stable codebase; no fast-moving external dependencies)
