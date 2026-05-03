---
phase: 03-sql-dialect-strategy
reviewed: 2026-05-03T00:00:00Z
depth: standard
files_reviewed: 9
files_reviewed_list:
  - src/Atomizer.EntityFrameworkCore/Providers/ISqlDialect.cs
  - src/Atomizer.EntityFrameworkCore/Providers/RelationalProviderCache.cs
  - src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlDialect.cs
  - src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlDialect.cs
  - src/Atomizer.EntityFrameworkCore/Providers/Sql/SqlServerDialect.cs
  - src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs
  - tests/Atomizer.EntityFrameworkCore.Tests/Providers/MySqlDialectTests.cs
  - tests/Atomizer.EntityFrameworkCore.Tests/Providers/PostgreSqlDialectTests.cs
  - tests/Atomizer.EntityFrameworkCore.Tests/Providers/SqlServerDialectTests.cs
findings:
  critical: 3
  warning: 5
  info: 2
  total: 10
status: issues_found
---

# Phase 03: Code Review Report

**Reviewed:** 2026-05-03T00:00:00Z
**Depth:** standard
**Files Reviewed:** 9
**Status:** issues_found

## Summary

This phase introduces the `ISqlDialect` abstraction with three concrete implementations (PostgreSQL, MySQL, SQL Server) and wires them into `EntityFrameworkCoreStorage` via `RelationalProviderCache`. The structural design — dialect polymorphism keyed on a provider enum, `EntityMap` for schema-aware column quoting — is sound in concept. However, all three dialect implementations share a critical SQL injection vulnerability introduced by misusing `FormattableStringFactory.Create`. There are also two additional blockers: locking hints are rendered ineffective outside a transaction, and two `IAtomizerStorage` interface methods throw `NotImplementedException` unconditionally, making them production-unusable stubs that compile and deploy silently. Several warnings around silent data loss, a broken log message, and public-surface violations round out the findings.

---

## Critical Issues

### CR-01: SQL Injection — `FormattableStringFactory.Create` Does Not Produce Parameterized SQL

**Files:**
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlDialect.cs:21-41`
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlDialect.cs:21-41`
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/SqlServerDialect.cs:21-39`
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlDialect.cs:47-57`
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlDialect.cs:47-55`
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/SqlServerDialect.cs:42-55`

**Issue:** All SQL in every dialect is constructed by calling `FormattableStringFactory.Create($"...")`. The C# interpolation inside that call is evaluated eagerly before `FormattableStringFactory.Create` ever runs — meaning the interpolated values (`queueKey`, `leaseToken.Token`, datetime strings) are already baked into the format string as plain text literals. `FormattableStringFactory.Create` receives a fully-resolved `string` as the first argument and creates a `FormattableString` with zero holes. When `FromSqlInterpolated` / `ExecuteSqlInterpolatedAsync` receives this object it correctly sees it has no parameters and passes the pre-built string to the database driver verbatim.

The result is that `queueKey`, `leaseToken.Token`, and date strings are concatenated directly into the SQL text. A `QueueKey` value of `default' OR '1'='1` or a `LeaseToken` containing a single quote terminates the string literal and injects arbitrary SQL into a live query.

The correct pattern is to write the interpolation directly in the call to `FromSqlInterpolated` / `ExecuteSqlInterpolatedAsync`, which intercepts the `FormattableString` before C# evaluates the holes and converts each hole into a `DbParameter`. Since `ISqlDialect` returns `FormattableString`, the fix is to have each dialect return a raw interpolated string literal using `$"..."` directly — C# will construct a `FormattableString` with holes intact — and pass that directly to EF Core.

**Fix:**

In each dialect method, replace:
```csharp
// BROKEN — interpolation is evaluated before FormattableStringFactory.Create runs
return FormattableStringFactory.Create(
    $"""
        SELECT t.* FROM {_jobs.Table} ...
        WHERE {c[nameof(...)]} = '{queueKey}'
        ...
    """
);
```

With a real interpolated string that uses EF Core parameters for user-supplied values:
```csharp
// CORRECT — C# constructs FormattableString with holes; EF Core maps them to DbParameters
return $"""
    SELECT t.*
    FROM {_jobs.Table} AS t
    WHERE {c[nameof(AtomizerJobEntity.QueueKey)]} = {queueKey.Key}
      AND ...
    LIMIT {batchSize}
    FOR UPDATE SKIP LOCKED
""";
```

Note: Table names and column names from `EntityMap` are trusted (they come from the EF Core model metadata, not from user input) and should remain as string-interpolated literals in the format template. Only user-supplied runtime values (`queueKey`, `leaseToken.Token`, datetime strings, `batchSize`) need to become holes that EF Core converts to parameters.

Note also that `batchSize` is an `int` and safe from string injection, but it must still be a hole (not inlined as text) to be treated as a parameter; for `TOP(N)` and `LIMIT N` syntax, EF Core may or may not support parameterized limits depending on the provider — verify at integration test time.

---

### CR-02: Locking Hints Are Ineffective Outside a Transaction

**Files:**
- `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:86-90`
- `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:212-221`

**Issue:** `GetDueJobsAsync` and `GetDueSchedulesAsync` execute `FOR NO KEY UPDATE SKIP LOCKED` (PostgreSQL), `FOR UPDATE SKIP LOCKED` (MySQL), and `WITH (UPDLOCK, READPAST, ROWLOCK)` (SQL Server) queries via `FromSqlInterpolated(...).AsNoTracking().ToListAsync(...)`. These queries run on the `DbContext`'s ambient connection with no surrounding transaction.

Row-level locking hints require an open transaction to hold the lock for any useful duration. Without a transaction:
- PostgreSQL / MySQL: the locks are acquired and released within the single statement execution, providing no meaningful mutual exclusion between competing polling instances.
- SQL Server: `UPDLOCK` without a surrounding transaction similarly does not prevent concurrent readers from acquiring their own locks on the same rows.

The effect is that multiple `QueuePump` instances (across nodes) can all poll and lease the same batch of jobs concurrently, creating duplicate processing. This defeats the entire purpose of the locking strategy.

**Fix:** Wrap the `GetDueJobs` SQL execution and the subsequent `UpdateJobsAsync` call in a single explicit database transaction (e.g., via `IDbContextTransaction` at `ReadCommitted` isolation or higher). The `DatabaseTransactionLeasingScope` referenced in `CLAUDE.md` exists precisely for this purpose — the dialect SQL and the job state update must occur inside the same transaction scope.

```csharp
// GetDueJobsAsync should execute inside the leasing transaction, not independently
await using var tx = await _dbContext.Database.BeginTransactionAsync(
    IsolationLevel.ReadCommitted, cancellationToken);

var entities = await JobEntities
    .FromSqlInterpolated(sql)
    .ToListAsync(cancellationToken); // no AsNoTracking — entities must be tracked for update

// ... update entities ...
await _dbContext.SaveChangesAsync(cancellationToken);
await tx.CommitAsync(cancellationToken);
```

---

### CR-03: `IAtomizerStorage.ExecuteInLeaseAsync` Throws `NotImplementedException` in Production Code

**File:** `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:241-258`

**Issue:** Both overloads of `ExecuteInLeaseAsync` unconditionally throw `NotImplementedException`. These are non-optional members of `IAtomizerStorage` — any caller that invokes them (e.g., `SchedulePoller` or `QueuePoller`) will crash at runtime with an unhandled `NotImplementedException`. Unlike `UpsertScheduleAsync` on the dialect interface (which is never called by the storage layer in this phase), `ExecuteInLeaseAsync` is a storage-contract method that callers are entitled to invoke.

This is not a placeholder in a test helper; it is in the production `IAtomizerStorage` implementation registered with the DI container. The EF Core storage backend is therefore non-functional for any operation that requires a lease scope.

**Fix:** If this method is genuinely deferred to Phase 4, document this with a note in `IAtomizerStorage` that the EF Core backend does not yet implement leasing. More importantly, guard the `EntityFrameworkCoreStorage` registration so it cannot be used in a configuration that would reach `ExecuteInLeaseAsync` before Phase 4 is complete. Alternatively, implement the minimum viable behavior now — wrapping the callback in a `ReadCommitted` transaction is already the stated design:

```csharp
public async Task<TResult> ExecuteInLeaseAsync<TResult>(
    QueueKey queue,
    Func<CancellationToken, Task<TResult>> callback,
    CancellationToken cancellationToken)
{
    await using var tx = await _dbContext.Database.BeginTransactionAsync(
        IsolationLevel.ReadCommitted, cancellationToken);
    var result = await callback(cancellationToken);
    await tx.CommitAsync(cancellationToken);
    return result;
}
```

---

## Warnings

### WR-01: `RelationalProviderCache.Instances` Is Keyed Only on `DatabaseProvider`, Causing Cross-`DbContext` Collision

**File:** `src/Atomizer.EntityFrameworkCore/Providers/RelationalProviderCache.cs:29-54`

**Issue:** `Instances` is a static `ConcurrentDictionary<DatabaseProvider, RelationalProviderCache>`. The key is only the detected provider enum value. If an application registers two different `DbContext` types against the same database provider (e.g., `AppDbContext` and `ReportingDbContext`, both using PostgreSQL but with different schemas or table names), only the first one to call `RelationalProviderCache.Create` will have its `EntityMap` stored. All subsequent calls for the same provider return the cached map from the first `DbContext`, which may reference the wrong schema, table names, or column names.

**Fix:** Key the cache by both provider and `DbContext` type:

```csharp
private static readonly ConcurrentDictionary<(DatabaseProvider, Type), RelationalProviderCache> Instances = new();

public static RelationalProviderCache Create<TDbContext>(TDbContext dbContext)
    where TDbContext : DbContext
{
    var provider = DetectProvider(dbContext.Database.ProviderName ?? string.Empty);
    var key = (provider, typeof(TDbContext));
    return Instances.GetOrAdd(key, _ => { ... });
}
```

---

### WR-02: `UpdateJobsAsync` and `UpdateSchedulesAsync` Silently Swallow `DbUpdateException`

**File:** `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:63-73` and `192-203`

**Issue:** Both methods catch `DbUpdateException`, log it, and then return normally. The caller (the processing pipeline) has no way to know the update failed. For `UpdateJobsAsync` this means jobs that failed to persist their state (e.g., `Completed`, `Failed`) will be re-leased and re-processed after the visibility timeout, causing duplicate execution. For `UpdateSchedulesAsync`, the `NextRunAt` advancement is silently lost, causing the schedule to fire again immediately on the next poll cycle.

The project's error-handling convention is: "Never let exceptions escape background loops — catch → log → continue." However, this convention applies to top-level loop bodies, not to storage calls within those loops. The storage contract should surface errors so the caller can decide whether to retry.

**Fix:** Re-throw after logging, or return a `bool`/`Result` so the caller can react. At minimum, document that callers must treat the returned `Task` as potentially indicating silent failure:

```csharp
catch (DbUpdateException ex)
{
    _logger.LogError(ex, "Failed to update jobs");
    throw; // let the pipeline decide how to handle
}
```

---

### WR-03: `UpsertScheduleAsync` Log Message Uses the Same Value for Both Placeholders

**File:** `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:180-186`

**Issue:** The error log call passes `schedule.JobKey` for both the `{ScheduleKey}` and `{JobKey}` template arguments:

```csharp
_logger.LogError(
    ex,
    "Failed to upsert schedule {ScheduleKey} for job {JobKey}",
    schedule.JobKey,   // ← ScheduleKey
    schedule.JobKey    // ← JobKey — duplicate, should be different
);
```

`AtomizerSchedule` does not have a separate `ScheduleKey` property — `JobKey` identifies both. The template name `{ScheduleKey}` implies they are distinct, which is misleading. The second argument is a redundant duplicate that adds noise to structured logs.

**Fix:** Remove the duplicate property and align the template to what is actually available:

```csharp
_logger.LogError(ex, "Failed to upsert schedule for job {JobKey}", schedule.JobKey);
```

---

### WR-04: `MySqlDialect` Truncates Datetime Precision to Seconds

**File:** `src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlDialect.cs:19` and `53` and `62`

**Issue:** `MySqlDialect` formats all datetimes as `now.ToString("yyyy-MM-dd HH:mm:ss")`, which silently truncates sub-second precision. PostgreSQL and SQL Server use the `"u"` format specifier (`yyyy-MM-dd HH:mm:ssZ`), which preserves seconds but also truncates sub-second values. MySQL 5.7+ and 8.x support fractional seconds (`DATETIME(6)`) and comparisons against fractional-second values work correctly.

The practical consequence is that a job with `ScheduledAt = 2026-05-03T10:00:00.500Z` will be visible in PostgreSQL/SqlServer queries (comparing against `10:00:00Z`) but will also appear visible in MySQL (comparing against `10:00:00`) even in the same instant — creating a 1-second window of inconsistent behavior across providers.

More critically, a job scheduled at `10:00:00.999Z` will appear due in MySQL at `10:00:00` (the truncated comparison time), causing it to be picked up up to one second early.

**Fix:** Use a format that includes fractional seconds for MySQL:

```csharp
var mySqlNow = now.ToString("yyyy-MM-dd HH:mm:ss.ffffff");
```

Or use `"o"` (roundtrip ISO 8601) and cast in SQL, but verify the MySQL driver accepts it. At minimum, document the precision limitation.

---

### WR-05: `EntityMap` and `DatabaseProvider` Are `public` — Internal Implementation Details Leaked

**Files:**
- `src/Atomizer.EntityFrameworkCore/Providers/EntityMap.cs:6`
- `src/Atomizer.EntityFrameworkCore/Providers/DatabaseProvider.cs:3`

**Issue:** Both `EntityMap` and `DatabaseProvider` are declared `public`. Per the project conventions, all provider infrastructure is internal (see `ISqlDialect` and `RelationalProviderCache` which are correctly `internal`). Marking these types public exposes them in the package's public API surface, which `EnablePackageValidation=true` will validate against, creates an implicit compatibility contract for consumers, and is inconsistent with every other type in `Providers/`.

**Fix:** Change both to `internal`:

```csharp
internal class EntityMap { ... }
internal enum DatabaseProvider { ... }
```

---

## Info

### IN-01: `ISqlDialect.UpsertScheduleAsync` Is a Deferred Stub That Will Throw on Any Call

**File:** `src/Atomizer.EntityFrameworkCore/Providers/ISqlDialect.cs:8`

**Issue:** `UpsertScheduleAsync` is declared on `ISqlDialect` but all three implementations unconditionally throw `NotImplementedException` with a `// TODO: Implemented in Phase 4` comment. The storage layer (`EntityFrameworkCoreStorage.UpsertScheduleAsync`) does not call this method — it uses EF Core directly — so this stub is currently unreachable. However, having it on the interface creates a false expectation that the interface is complete. Any future code that resolves `ISqlDialect` and calls `UpsertScheduleAsync` (e.g., if a developer follows the established pattern) will crash.

**Fix:** Either remove `UpsertScheduleAsync` from `ISqlDialect` now and add it back in Phase 4, or add a `// NOTE: Not yet used by EntityFrameworkCoreStorage — will be wired in Phase 4` comment clearly on the interface method to signal intentional incompleteness.

---

### IN-02: Tests Validate SQL Keyword Presence Only — Parameterization and Correctness Not Verified

**Files:**
- `tests/Atomizer.EntityFrameworkCore.Tests/Providers/MySqlDialectTests.cs`
- `tests/Atomizer.EntityFrameworkCore.Tests/Providers/PostgreSqlDialectTests.cs`
- `tests/Atomizer.EntityFrameworkCore.Tests/Providers/SqlServerDialectTests.cs`

**Issue:** All three test files check that the returned `FormattableString.Format` contains certain SQL keyword strings (`FOR UPDATE SKIP LOCKED`, `WITH (UPDLOCK, READPAST, ROWLOCK)`, `UPDATE`, `LIMIT`, `TOP(`). They do not verify that user-supplied values (`queueKey`, `leaseToken.Token`) appear as holes (`{0}`, `{1}`) in `Format` rather than as inlined string literals. This means the SQL injection vulnerability described in CR-01 passes all tests.

**Fix:** Add assertions that `sql.ArgumentCount > 0` and that `sql.Format` does not contain the literal value of `queueKey` or `leaseToken.Token`:

```csharp
// Assert that queueKey is parameterized, not inlined
sql.ArgumentCount.Should().BeGreaterThan(0);
sql.Format.Should().NotContain(QueueKey.Default.Key); // "default" should not appear verbatim
```

---

_Reviewed: 2026-05-03T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
