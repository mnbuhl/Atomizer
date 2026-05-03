---
phase: 04-ef-core-implementation
reviewed: 2026-05-03T00:00:00Z
depth: standard
files_reviewed: 7
files_reviewed_list:
  - src/Atomizer.EntityFrameworkCore/Configurations/AtomizerScheduleEntityConfiguration.cs
  - src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlDialect.cs
  - src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlDialect.cs
  - src/Atomizer.EntityFrameworkCore/Providers/Sql/SqlServerDialect.cs
  - src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreJobStorageOptions.cs
  - src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs
  - tests/Atomizer.EntityFrameworkCore.Tests/Fixtures/BaseDatabaseFixture.cs
findings:
  critical: 4
  warning: 5
  info: 3
  total: 12
status: issues_found
---

# Phase 04: Code Review Report

**Reviewed:** 2026-05-03T00:00:00Z
**Depth:** standard
**Files Reviewed:** 7
**Status:** issues_found

## Summary

The implementation covers the EF Core storage backend including three SQL dialect providers (PostgreSQL, MySQL, SQL Server), entity configuration, the main storage class, and the database fixture. The architecture is sound and the dialect dispatch via `RelationalProviderCache` works correctly. However, there are four blockers: all three `UpsertScheduleAsync` dialect methods embed user-controlled string values directly into raw SQL without escaping, a `FormattableStringFactory.Create` anti-pattern that defeats EF Core's parameterization safety net, a silent data-loss swallow in `UpdateJobsAsync`, and a `DateTimeOffset.UtcNow` call inside the dialect layer that violates the project's mandatory clock abstraction. Five warnings cover a job-ID round-trip inconsistency on upsert for supported providers, a constraint mismatch between the domain model and the schema, missing XML docs on a public configuration class constructor, an `EnsureCreatedAsync` choice in test infrastructure that skips migrations, and a stale `@todo` comment in production code.

---

## Critical Issues

### CR-01: SQL injection via unescaped string embedding in all three `UpsertScheduleAsync` methods

**Files:**
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlDialect.cs:103-107`
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlDialect.cs:100-106`
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/SqlServerDialect.cs:84-86, 112-117`

**Issue:** `UpsertScheduleAsync` embeds `entity.JobKey`, `entity.QueueKey`, `entity.PayloadType`, `entity.Payload`, `entity.Schedule`, `entity.TimeZone`, and `entity.RetryIntervals` directly as single-quoted SQL string literals with no escaping. A payload value containing a single quote — which is common in JSON (e.g., `{"key":"it's a value"}`) — will break the query. A deliberately crafted `JobKey`, `PayloadType`, or `Payload` value that contains `'` followed by SQL can modify the statement's structure.

The `GetDueJobs` and `ReleaseLeasedJobs` methods are safe because they only embed `queueKey` (constrained to 100 chars, no SQL-sensitive chars), integer enum values, and date-time strings. `UpsertScheduleAsync` is the only method that embeds arbitrary user-controlled strings.

Note: `FormattableStringFactory.Create(...)` with a `$"..."` argument where all interpolation has already been expanded into the format string is treated by EF Core as a raw SQL call with zero parameters — the `IFormattable` safety mechanism never fires. This is confirmed by the fact that all interpolated values are resolved before `FormattableStringFactory.Create` is called, producing a string with no format holes.

**Fix:** Use `FormattableString` interpolation directly (i.e., `return $"..."`) instead of `FormattableStringFactory.Create(...)`. EF Core's `FromSqlInterpolated` / `ExecuteSqlInterpolatedAsync` only parameterize holes in a genuine C# interpolated string; switch every string-valued column to a format hole:

```csharp
// PostgreSqlDialect — use real interpolation so EF Core parameterizes each {expr}
public FormattableString UpsertScheduleAsync(AtomizerSchedule schedule)
{
    var entity = schedule.ToEntity();
    var pgNow = DateTimeOffset.UtcNow.ToString("u");
    var c = _schedules.Col;
    // Return a real FormattableString — EF Core will turn each {} into a $1/$2/... parameter
    return $"""
        INSERT INTO {_schedules.Table} (
            {c[nameof(AtomizerScheduleEntity.JobKey)]},
            ...
        ) VALUES (
            {entity.JobKey},
            ...
        )
        ON CONFLICT ({c[nameof(AtomizerScheduleEntity.JobKey)]}) DO UPDATE SET
            ...
        """;
}
```

Apply the same pattern to all three dialects for every string-valued entity field. Integer and boolean literals (`{(int)entity.MisfirePolicy}`, `{entity.MaxCatchUp}`) are safe as-is because they cannot contain SQL syntax.

---

### CR-02: `FormattableStringFactory.Create` defeats parameterization across all dialect methods

**Files:**
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlDialect.cs:21,47,64,83`
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlDialect.cs:21,47,64,81`
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/SqlServerDialect.cs:21,45,62,78`

**Issue:** All dialect methods use the pattern:
```csharp
return FormattableStringFactory.Create($"... {interpolated} ...");
```
When a C# interpolated string `$"..."` is passed as the first argument to `FormattableStringFactory.Create(string format, params object[] args)`, the compiler evaluates the interpolation eagerly, converting every `{expr}` to a string and producing a fully-resolved `string` with no remaining holes. `FormattableStringFactory.Create` then wraps it as a `FormattableString` with zero arguments. EF Core therefore sees a literal SQL string and does not parameterize anything.

This means even the date-time strings, queue key comparisons, and lease token comparisons in `GetDueJobs` and `ReleaseLeasedJobs` are embedded literally — they happen to be safe today only because those values are constrained (enums, dates, well-validated value objects). But the pattern is architecturally broken: the return type `FormattableString` signals parameterization intent that is silently not delivered.

**Fix:** Remove `FormattableStringFactory.Create(...)` wrappers. Return real C# interpolated strings directly. For values that must be literal SQL identifiers (table names, column names), assign them to local variables first:
```csharp
// Before — ALL interpolation is resolved before Create() sees it:
return FormattableStringFactory.Create($"SELECT * FROM {_jobs.Table} WHERE id = '{someId}'");

// After — EF Core sees the holes and parameterizes {someId}:
var table = _jobs.Table;          // string literal, safe as SQL identifier
return $"SELECT * FROM {table} WHERE id = {someId}";
```
Column and table name strings from `EntityMap` are constructed from `IModel` metadata with provider-specific quoting, so they are safe as literal SQL identifiers.

---

### CR-03: `UpdateJobsAsync` and `UpdateSchedulesAsync` silently swallow `DbUpdateException`, losing job state

**File:** `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:62-73, 202-213`

**Issue:** Both `UpdateJobsAsync` and `UpdateSchedulesAsync` catch `DbUpdateException`, log the error, and return normally. The callers (`JobProcessor`, `ScheduleProcessor`) have no way to detect failure and will proceed as if the state transition succeeded. For a job that just completed, this means `Completed` status is never persisted — the job reverts to `Pending` on the next visibility-timeout expiry and executes again, violating at-most-once delivery. For a failed job, retries are silently lost.

```csharp
// Current — caller cannot know the update failed
catch (DbUpdateException ex)
{
    _logger.LogError(ex, "Failed to update jobs");
    // returns normally — caller proceeds as if update succeeded
}
```

**Fix:** Re-throw the exception after logging so the caller can handle or propagate it. Alternatively, return a `bool` / `Result` from the method. At a minimum the exception must not be swallowed:
```csharp
catch (DbUpdateException ex)
{
    _logger.LogError(ex, "Failed to update jobs");
    throw; // let JobProcessor apply retry / failure logic
}
```

---

### CR-04: `DateTimeOffset.UtcNow` called directly inside all three dialect `UpsertScheduleAsync` methods

**Files:**
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlDialect.cs:79`
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlDialect.cs:81`
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/SqlServerDialect.cs:76`

**Issue:** The project mandates that pipeline code must never call `DateTimeOffset.UtcNow` directly — only via `IAtomizerClock`. All three `UpsertScheduleAsync` implementations compute `now` as `DateTimeOffset.UtcNow.ToString(...)` to stamp `UpdatedAt`. This makes the timestamp untestable and inconsistent: the dialect's `now` will differ from the `now` already computed by the caller (`EntityFrameworkCoreStorage`) when `UpdateSchedulesAsync` uses `UpdateRange`. More concretely, integration tests that freeze the clock will see a real wall-clock timestamp in `UpdatedAt` rather than the test clock's value.

**Fix:** Remove the local `now` variable from each dialect's `UpsertScheduleAsync` and add a `DateTimeOffset now` parameter to `ISqlDialect.UpsertScheduleAsync`, passing the caller's already-resolved clock value through:
```csharp
// ISqlDialect
FormattableString UpsertScheduleAsync(AtomizerSchedule schedule, DateTimeOffset now);

// EntityFrameworkCoreStorage.UpsertScheduleAsync
var now = _clock.UtcNow;  // IAtomizerClock injected into storage
var sql = _providerCache.Dialect.UpsertScheduleAsync(schedule, now);
```

---

## Warnings

### WR-01: `UpsertScheduleAsync` on supported providers cannot reliably return the persisted entity ID

**File:** `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:155-163`

**Issue:** On supported providers (PostgreSQL, MySQL, SQL Server) the method returns `entity.Id` where `entity` was produced by `schedule.ToEntity()` at line 157, before the SQL is executed. For an INSERT this is correct because `Id` is generated client-side (`Guid.NewGuid()` or equivalent in `ToEntity()`). But for the UPDATE (conflict) path, the actual stored row may have a different `Id` — the one from the original INSERT. The caller receiving the returned `Guid` will believe it holds the canonical ID for this schedule, but may be getting a freshly-generated Guid that was never written.

```csharp
var entity = schedule.ToEntity();   // new Guid generated here
// ...
await _dbContext.Database.ExecuteSqlInterpolatedAsync(sql, cancellationToken);
return entity.Id;   // this Guid was NOT inserted if the conflict path executed
```

**Fix:** For the upsert conflict path, return the ID from the ON CONFLICT / MERGE logic using `RETURNING id` (PostgreSQL) or `OUTPUT inserted.Id` (SQL Server). Alternatively, follow the unsafe-fallback path's strategy: query the row by `JobKey` after the upsert to retrieve the canonical `Id`. The simplest safe fix is to have `AtomizerSchedule` carry a stable `Id` that was already persisted (which the caller should supply on update), removing the need to return a newly-generated one.

---

### WR-02: `JobKey` schema column is 512 chars; domain model enforces 255 chars — migration will accept oversized keys

**File:** `src/Atomizer.EntityFrameworkCore/Configurations/AtomizerScheduleEntityConfiguration.cs:25`

**Issue:** `CLAUDE.md` states job keys are `≤255 chars`. `JobKey.cs` validates `key.Length > 255` and throws. The `AtomizerSchedules` table maps `JobKey` as `HasMaxLength(512)`. This is not a data-loss risk in the current direction (domain enforces the tighter bound), but it is a correctness mismatch: the schema doubles the domain's limit. Any tooling that writes directly to the table (migrations, manual inserts, future non-.NET consumers) can create rows with keys 256–512 chars that the domain will reject on read-back, causing `InvalidJobKeyException` during `ToAtomizerSchedule()`.

**Fix:** Align the column length to the domain constraint:
```csharp
builder.Property(e => e.JobKey).IsRequired().HasMaxLength(255);
```
The same mismatch exists for `QueueKey` (`≤100` chars in `QueueKey.cs` vs `HasMaxLength(512)` in both configuration files) and for `LeaseToken` (format-constrained in the value object vs `HasMaxLength(512)` in `AtomizerJobEntityConfiguration`).

---

### WR-03: `GetDueJobsAsync` LINQ fallback path does not lock rows — concurrent workers will double-process jobs

**File:** `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:93-110`

**Issue:** The `AllowUnsafeProviderFallback` LINQ path reads jobs with `AsNoTracking()` and no advisory lock or optimistic-concurrency stamp. Two concurrent polling workers on the same queue will receive the same batch of `Pending` jobs. The documentation comment on `AllowUnsafeProviderFallback` warns about "unexpected behavior in distributed environments," but does not warn that it is also broken for a single multi-queue-pump process. The `InMemoryLeasingScopeFactory` per-queue semaphore is not used here because `DatabaseTransactionLeasingScope` is the scope factory when EF storage is active. The transaction opened by `ExecuteInLeaseAsync` does not cover the LINQ query's snapshot.

**Fix:** Add a code comment explicitly naming this specific hazard so consumers of the option understand it is unsound even on a single node:
```csharp
// WARNING: AsNoTracking() with no row lock means two concurrent QueuePumps
// on the same process (or any second node) will both receive the same jobs.
// AllowUnsafeProviderFallback is only safe with DegreeOfParallelism=1 and
// a single process instance. It is not safe for production use.
```
Or, more robustly: apply an optimistic concurrency check by stamping `LeaseToken` in the read transaction before returning, then filtering on `LeaseToken IS NULL` at read time.

---

### WR-04: `DatabaseTransactionLeasingScope` commits the transaction on `Dispose` / `DisposeAsync` even when the callback threw

**File:** `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScope.cs:22-63`

**Issue:** `EntityFrameworkCoreStorage.ExecuteInLeaseAsync` does:
```csharp
await using var scope = await DatabaseTransactionLeasingScope.StartTransaction(...);
if (!scope.Acquired) return default!;
return await callback(cancellationToken);  // if this throws, DisposeAsync still commits
```
If `callback` throws, the `await using` causes `DisposeAsync` to run, which calls `CommitAsync`. The `catch` in `DisposeAsync` only catches exceptions thrown by `CommitAsync` itself — it does not know whether the callback succeeded. Any partial writes made inside the callback (e.g., a `SaveChangesAsync` inside `GetDueJobsAsync`) will be committed even after the callback threw.

In the current code the raw SQL in `GetDueJobsAsync` only reads, so this is not immediately exploitable. However `GetDueSchedulesAsync` called inside the `SchedulePoller`'s lease may interleave with follow-up `UpdateSchedulesAsync` calls that are outside the lease transaction, making the commit semantic surprising.

**Fix:** Track whether the lease body completed without exception and only commit on clean exit:
```csharp
public async Task<TResult> ExecuteInLeaseAsync<TResult>(...)
{
    await using var scope = await DatabaseTransactionLeasingScope.StartTransaction(...);
    if (!scope.Acquired) return default!;
    TResult result;
    try
    {
        result = await callback(cancellationToken);
    }
    catch
    {
        scope.Abort(); // new method that sets a flag to rollback in DisposeAsync
        throw;
    }
    return result;
}
```

---

### WR-05: `BaseDatabaseFixture` uses `EnsureCreatedAsync` instead of `MigrateAsync` — tests do not validate migration scripts

**File:** `tests/Atomizer.EntityFrameworkCore.Tests/Fixtures/BaseDatabaseFixture.cs:26`

**Issue:** `EnsureCreatedAsync` creates the schema by directly applying the EF Core model snapshot, bypassing all migration scripts. If a migration script contains a bug (wrong column type, missing index, incorrect default), the integration tests will pass anyway because they never run the migration. The schema the tests validate against is the EF Core model — not the schema that production deployments actually create.

**Fix:** Replace with `MigrateAsync()`:
```csharp
await DbContext.Database.MigrateAsync();
```
If the project does not currently generate migrations (using `EnsureCreatedAsync` intentionally), add a comment explaining why, and add at least one test that verifies the migration produces the same schema as the model snapshot.

---

## Info

### IN-01: `AtomizerScheduleEntityConfiguration` constructor and `Configure` method lack XML documentation

**File:** `src/Atomizer.EntityFrameworkCore/Configurations/AtomizerScheduleEntityConfiguration.cs:15,20`

**Issue:** The class has a `<summary>` tag on the class, but the constructor and `Configure` method are `public` and have no `<param>` or `<summary>` documentation. The project's `CLAUDE.md` requires `<summary>` on all public APIs with `<param>` for every parameter.

**Fix:**
```csharp
/// <summary>
/// Initializes a new instance of <see cref="AtomizerScheduleEntityConfiguration"/>.
/// </summary>
/// <param name="schema">Optional database schema name. Pass <c>null</c> to use the default schema.</param>
public AtomizerScheduleEntityConfiguration(string? schema) { ... }

/// <summary>
/// Configures the entity type mapping for <see cref="AtomizerScheduleEntity"/>.
/// </summary>
/// <param name="builder">The entity type builder.</param>
public void Configure(EntityTypeBuilder<AtomizerScheduleEntity> builder) { ... }
```

---

### IN-02: Stale `@todo` comment in production code (`InsertAsync`)

**File:** `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:37`

**Issue:** `// @todo: make idempotency key unique with index` is present in shipping code. The absence of a database-level unique constraint on `IdempotencyKey` means that two concurrent `InsertAsync` calls with the same key that both pass the `FirstOrDefaultAsync` check before either commits will insert duplicate rows — a TOCTOU race. The comment acknowledges this but does not have a tracking issue reference.

**Fix:** Add a unique index on `IdempotencyKey` in `AtomizerJobEntityConfiguration` (sparse/filtered index where `IdempotencyKey IS NOT NULL` on SQL Server and PostgreSQL), or add an `IGNORE_DUP_KEY` hint for SQL Server. Remove the comment once the index is in place.

---

### IN-03: `MySqlDialect.GetDueSchedules` uses `SELECT *` while `PostgreSqlDialect` and `SqlServerDialect` use `SELECT t.*`

**File:** `src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlDialect.cs:66`

**Issue:** Minor inconsistency. `GetDueSchedules` in MySQL uses `SELECT *` without a table alias, while the other two dialects alias the table as `t` and use `SELECT t.*`. The behavior is identical in this case (single-table query), but the inconsistency makes the codebase harder to extend uniformly if a JOIN is ever added.

**Fix:** Align to the pattern used by the other dialects:
```sql
SELECT t.*
FROM {_schedules.Table} AS t
WHERE ...
FOR UPDATE SKIP LOCKED;
```

---

_Reviewed: 2026-05-03T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
