# Phase 4: EF Core Implementation - Pattern Map

**Mapped:** 2026-05-03
**Files analyzed:** 8 new/modified files
**Analogs found:** 8 / 8

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` | storage | request-response + CRUD | self (existing file, partial impl) | self |
| `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreJobStorageOptions.cs` | config | — | self (existing file, add property) | self |
| `src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlDialect.cs` | provider/SQL | request-response | `PostgreSqlDialect.GetDueJobs` / `ReleaseLeasedJobs` (same file) | exact (same file, same method shape) |
| `src/Atomizer.EntityFrameworkCore/Providers/Sql/SqlServerDialect.cs` | provider/SQL | request-response | `SqlServerDialect.GetDueJobs` / `ReleaseLeasedJobs` (same file) | exact (same file, same method shape) |
| `src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlDialect.cs` | provider/SQL | request-response | `MySqlDialect.GetDueJobs` / `ReleaseLeasedJobs` (same file) | exact (same file, same method shape) |
| `src/Atomizer.EntityFrameworkCore/Configurations/AtomizerScheduleEntityConfiguration.cs` | config | — | `AtomizerJobEntityConfiguration.cs` | role-match |
| `tests/Atomizer.EntityFrameworkCore.Tests/Fixtures/BaseDatabaseFixture.cs` | test infrastructure | — | self (existing file, one-line change) | self |
| `tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/` (delete migrations + factories) | — | — | — | deletion only |

---

## Pattern Assignments

### `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs`

**Analog:** Self — `GetDueJobsAsync` (lines 75–117) for the provider-guard pattern, and `DatabaseTransactionLeasingScope.StartTransaction` for transaction ownership.

#### Provider-guard pattern used by GetDueJobsAsync and GetDueSchedulesAsync (lines 84–116)

Copy this guard structure verbatim for `UpsertScheduleAsync` and also wire it into `ExecuteInLeaseAsync`:

```csharp
if (_providerCache is { IsSupportedProvider: true, Dialect: not null })
{
    var sql = _providerCache.Dialect.GetDueJobs(queueKey, now, batchSize);
    var entities = await JobEntities.FromSqlInterpolated(sql).AsNoTracking().ToListAsync(cancellationToken);
    return entities.Select(job => job.ToAtomizerJob()).ToList();
}

if (!_providerCache.IsSupportedProvider && _options.AllowUnsafeProviderFallback)
{
    // LINQ fallback ...
}

throw new NotSupportedException(
    "The current database provider is not supported. "
        + "To bypass this check, set AllowUnsafeProviderFallback to true in EntityFrameworkCoreJobStorageOptions. "
        + "Note that this may lead to unexpected behavior."
);
```

#### ExecuteSqlInterpolatedAsync call pattern used by ReleaseLeasedAsync (lines 125–129)

Upsert is a non-SELECT DML statement — use `ExecuteSqlInterpolatedAsync`, not `FromSqlInterpolated`:

```csharp
if (_providerCache is { IsSupportedProvider: true, Dialect: not null })
{
    var sql = _providerCache.Dialect.ReleaseLeasedJobs(leaseToken, now);
    var result = await _dbContext.Database.ExecuteSqlInterpolatedAsync(sql, cancellationToken);
    return result;
}
```

#### ExecuteInLeaseAsync implementation sketch (lines 241–259 — currently NotImplementedException)

Use `DatabaseTransactionLeasingScope.StartTransaction` (lines 65–84 of `DatabaseTransactionLeasingScope.cs`). Pattern from D-01/D-02/D-03 in CONTEXT.md:

```csharp
public async Task<TResult> ExecuteInLeaseAsync<TResult>(
    QueueKey queue,
    Func<CancellationToken, Task<TResult>> callback,
    CancellationToken cancellationToken)
{
    await using var scope = await DatabaseTransactionLeasingScope.StartTransaction(
        _dbContext, _options.LockTimeout, cancellationToken);

    if (!scope.Acquired)
        return default!;

    return await callback(cancellationToken);
}

public async Task ExecuteInLeaseAsync(
    QueueKey queue,
    Func<CancellationToken, Task> callback,
    CancellationToken cancellationToken)
{
    await using var scope = await DatabaseTransactionLeasingScope.StartTransaction(
        _dbContext, _options.LockTimeout, cancellationToken);

    if (!scope.Acquired)
        return;

    await callback(cancellationToken);
}
```

`DisposeAsync()` (lines 39–63 of `DatabaseTransactionLeasingScope.cs`) commits on success and rolls back on exception — no explicit try/catch needed in `ExecuteInLeaseAsync`.

#### UpsertScheduleAsync revised structure (lines 155–190)

Apply provider-guard pattern with `ExecuteSqlInterpolatedAsync` for the supported path; keep EF check-then-insert for the fallback path with the required comment:

```csharp
public async Task<Guid> UpsertScheduleAsync(AtomizerSchedule schedule, CancellationToken cancellationToken)
{
    var entity = schedule.ToEntity();

    if (_providerCache is { IsSupportedProvider: true, Dialect: not null })
    {
        var sql = _providerCache.Dialect.UpsertScheduleAsync(schedule);
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(sql, cancellationToken);
        return entity.Id;
    }

    if (!_providerCache.IsSupportedProvider && _options.AllowUnsafeProviderFallback)
    {
        // Not race-safe — only used for test-only providers (SQLite) via AllowUnsafeProviderFallback
        var existing = await ScheduleEntities
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.JobKey == entity.JobKey, cancellationToken);

        if (existing is not null)
        {
            entity.Id = existing.Id;
            ScheduleEntities.Update(entity);
        }
        else
        {
            ScheduleEntities.Add(entity);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to upsert schedule for job {JobKey}", schedule.JobKey);
        }

        return entity.Id;
    }

    throw new NotSupportedException(
        "The current database provider is not supported. "
            + "To bypass this check, set AllowUnsafeProviderFallback to true in EntityFrameworkCoreJobStorageOptions. "
            + "Note that this may lead to unexpected behavior."
    );
}
```

#### AsNoTracking removal from GetDueJobsAsync (line 88)

Per D-04: remove `AsNoTracking()` from the `FromSqlInterpolated` chain inside `GetDueJobsAsync`. The LINQ fallback path on lines 95–109 also uses `AsNoTracking()` — leave that one in place (no transaction context there). The `GetDueSchedulesAsync` analog on line 218 is a SELECT-only path; leave its `AsNoTracking()` since schedules are not updated within the same transaction.

---

### `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreJobStorageOptions.cs`

**Analog:** Self — existing `AllowUnsafeProviderFallback` property (lines 6–11) as the XML-doc style to copy.

#### Existing property XML-doc style (lines 6–11)

```csharp
/// <summary>
/// If true, allows falling back to providers that may not be
/// fully supported, tested or work in distributed environments (e.g. SQLite).
/// <remarks>Default is false. See documentation for details and implications.</remarks>
/// </summary>
public bool AllowUnsafeProviderFallback { get; set; } = false;
```

Add `LockTimeout` using the same style. The property name `LockTimeout` is preferred (shortest, unambiguous). Default 30 seconds per D-02:

```csharp
/// <summary>
/// Maximum time to wait when acquiring a database transaction lock before giving up.
/// </summary>
/// <remarks>Default is 30 seconds. If acquisition times out, the polling tick is skipped and retried on the next interval.</remarks>
public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(30);
```

---

### `src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlDialect.cs`

**Analog:** `GetDueSchedules` in the same file (lines 60–76) — same `_schedules.Col` + `FormattableStringFactory.Create` pattern with a raw SQL string literal.

#### FormattableStringFactory.Create pattern with _schedules.Col (lines 60–76)

```csharp
public FormattableString GetDueSchedules(DateTimeOffset now)
{
    var pgNow = now.ToString("u");
    var c = _schedules.Col;
    return FormattableStringFactory.Create(
        $"""
            SELECT t.*
            FROM {_schedules.Table} AS t
            WHERE {c[nameof(AtomizerScheduleEntity.Enabled)]} = TRUE
              AND {c[nameof(AtomizerScheduleEntity.NextRunAt)]} <= '{pgNow}'
            ORDER BY {c[nameof(AtomizerScheduleEntity.NextRunAt)]}, {c[nameof(AtomizerScheduleEntity.Id)]}
            FOR NO KEY UPDATE SKIP LOCKED;
        """
    );
}
```

#### UpsertScheduleAsync target pattern for PostgreSQL

Replace the `NotImplementedException` stub (lines 78–82). Use `INSERT ... ON CONFLICT (job_key) DO UPDATE SET`. The column name for `JobKey` is accessed via `c[nameof(AtomizerScheduleEntity.JobKey)]` — EntityMap resolves the actual quoted column name from the EF model (including the schema-qualified table name in `_schedules.Table`). Use `now.ToString("u")` format consistent with all other timestamp formatting in the file:

```csharp
public FormattableString UpsertScheduleAsync(AtomizerSchedule schedule)
{
    var entity = schedule.ToEntity();
    var pgNow = DateTimeOffset.UtcNow.ToString("u");
    var c = _schedules.Col;
    return FormattableStringFactory.Create(
        $"""
            INSERT INTO {_schedules.Table} (
                {c[nameof(AtomizerScheduleEntity.Id)]},
                {c[nameof(AtomizerScheduleEntity.JobKey)]},
                {c[nameof(AtomizerScheduleEntity.QueueKey)]},
                {c[nameof(AtomizerScheduleEntity.PayloadType)]},
                {c[nameof(AtomizerScheduleEntity.Payload)]},
                {c[nameof(AtomizerScheduleEntity.Schedule)]},
                {c[nameof(AtomizerScheduleEntity.TimeZone)]},
                {c[nameof(AtomizerScheduleEntity.MisfirePolicy)]},
                {c[nameof(AtomizerScheduleEntity.MaxCatchUp)]},
                {c[nameof(AtomizerScheduleEntity.Enabled)]},
                {c[nameof(AtomizerScheduleEntity.RetryIntervals)]},
                {c[nameof(AtomizerScheduleEntity.NextRunAt)]},
                {c[nameof(AtomizerScheduleEntity.LastEnqueueAt)]},
                {c[nameof(AtomizerScheduleEntity.CreatedAt)]},
                {c[nameof(AtomizerScheduleEntity.UpdatedAt)]}
            ) VALUES (
                '{entity.Id}', '{entity.JobKey}', '{entity.QueueKey}',
                '{entity.PayloadType}', '{entity.Payload}', '{entity.Schedule}',
                '{entity.TimeZone}', {(int)entity.MisfirePolicy}, {entity.MaxCatchUp},
                {(entity.Enabled ? "TRUE" : "FALSE")},
                '{string.Join(";", entity.RetryIntervals.Select(ts => (long)ts.TotalMilliseconds))}',
                '{entity.NextRunAt:u}',
                {(entity.LastEnqueueAt.HasValue ? $"'{entity.LastEnqueueAt:u}'" : "NULL")},
                '{entity.CreatedAt:u}', '{pgNow}'
            )
            ON CONFLICT ({c[nameof(AtomizerScheduleEntity.JobKey)]}) DO UPDATE SET
                {c[nameof(AtomizerScheduleEntity.QueueKey)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.QueueKey)]},
                {c[nameof(AtomizerScheduleEntity.PayloadType)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.PayloadType)]},
                {c[nameof(AtomizerScheduleEntity.Payload)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.Payload)]},
                {c[nameof(AtomizerScheduleEntity.Schedule)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.Schedule)]},
                {c[nameof(AtomizerScheduleEntity.TimeZone)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.TimeZone)]},
                {c[nameof(AtomizerScheduleEntity.MisfirePolicy)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.MisfirePolicy)]},
                {c[nameof(AtomizerScheduleEntity.MaxCatchUp)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.MaxCatchUp)]},
                {c[nameof(AtomizerScheduleEntity.Enabled)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.Enabled)]},
                {c[nameof(AtomizerScheduleEntity.RetryIntervals)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.RetryIntervals)]},
                {c[nameof(AtomizerScheduleEntity.NextRunAt)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.NextRunAt)]},
                {c[nameof(AtomizerScheduleEntity.UpdatedAt)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.UpdatedAt)]};
        """
    );
}
```

Note: `ON CONFLICT` requires the column name as it appears in the database (not the C# property name). The `EntityMap.Col` dictionary values already contain the correctly quoted column names (e.g. `"job_key"` for PostgreSQL). The planner must verify the actual column name generated by EF for `JobKey` in `AtomizerSchedules` — with no `HasColumnName` override in the configuration, EF Core uses the property name directly (convention: `JobKey` → column `JobKey` or snake_case depending on the naming convention configured). Check the existing Postgres migration snapshot for the definitive column name.

---

### `src/Atomizer.EntityFrameworkCore/Providers/Sql/SqlServerDialect.cs`

**Analog:** `GetDueJobs` and `GetDueSchedules` in the same file (lines 17–69) for the `FormattableStringFactory.Create` + `_schedules.Col` pattern with SQL Server timestamp format `now.ToString("u")`.

#### UpsertScheduleAsync target pattern for SQL Server

Replace the `NotImplementedException` stub (lines 73–77). SQL Server requires `MERGE ... USING (VALUES ...) AS src ON target.job_key = src.job_key WHEN MATCHED THEN UPDATE ... WHEN NOT MATCHED THEN INSERT ...`. SQL Server uses `WITH (UPDLOCK, ROWLOCK)` for locking (see `GetDueJobs` line 24 for the hint style):

```csharp
public FormattableString UpsertScheduleAsync(AtomizerSchedule schedule)
{
    var entity = schedule.ToEntity();
    var sqlServerNow = DateTimeOffset.UtcNow.ToString("u");
    var c = _schedules.Col;
    return FormattableStringFactory.Create(
        $"""
            MERGE {_schedules.Table} WITH (HOLDLOCK) AS target
            USING (SELECT '{entity.JobKey}') AS src ({c[nameof(AtomizerScheduleEntity.JobKey)]})
            ON target.{c[nameof(AtomizerScheduleEntity.JobKey)]} = src.{c[nameof(AtomizerScheduleEntity.JobKey)]}
            WHEN MATCHED THEN UPDATE SET
                {c[nameof(AtomizerScheduleEntity.QueueKey)]} = '{entity.QueueKey}',
                {c[nameof(AtomizerScheduleEntity.PayloadType)]} = '{entity.PayloadType}',
                {c[nameof(AtomizerScheduleEntity.Payload)]} = '{entity.Payload}',
                {c[nameof(AtomizerScheduleEntity.Schedule)]} = '{entity.Schedule}',
                {c[nameof(AtomizerScheduleEntity.TimeZone)]} = '{entity.TimeZone}',
                {c[nameof(AtomizerScheduleEntity.MisfirePolicy)]} = {(int)entity.MisfirePolicy},
                {c[nameof(AtomizerScheduleEntity.MaxCatchUp)]} = {entity.MaxCatchUp},
                {c[nameof(AtomizerScheduleEntity.Enabled)]} = {(entity.Enabled ? 1 : 0)},
                {c[nameof(AtomizerScheduleEntity.RetryIntervals)]} = '{string.Join(";", entity.RetryIntervals.Select(ts => (long)ts.TotalMilliseconds))}',
                {c[nameof(AtomizerScheduleEntity.NextRunAt)]} = '{entity.NextRunAt:u}',
                {c[nameof(AtomizerScheduleEntity.UpdatedAt)]} = '{sqlServerNow}'
            WHEN NOT MATCHED THEN INSERT (
                {c[nameof(AtomizerScheduleEntity.Id)]},
                {c[nameof(AtomizerScheduleEntity.JobKey)]},
                {c[nameof(AtomizerScheduleEntity.QueueKey)]},
                {c[nameof(AtomizerScheduleEntity.PayloadType)]},
                {c[nameof(AtomizerScheduleEntity.Payload)]},
                {c[nameof(AtomizerScheduleEntity.Schedule)]},
                {c[nameof(AtomizerScheduleEntity.TimeZone)]},
                {c[nameof(AtomizerScheduleEntity.MisfirePolicy)]},
                {c[nameof(AtomizerScheduleEntity.MaxCatchUp)]},
                {c[nameof(AtomizerScheduleEntity.Enabled)]},
                {c[nameof(AtomizerScheduleEntity.RetryIntervals)]},
                {c[nameof(AtomizerScheduleEntity.NextRunAt)]},
                {c[nameof(AtomizerScheduleEntity.LastEnqueueAt)]},
                {c[nameof(AtomizerScheduleEntity.CreatedAt)]},
                {c[nameof(AtomizerScheduleEntity.UpdatedAt)]}
            ) VALUES (
                '{entity.Id}', '{entity.JobKey}', '{entity.QueueKey}',
                '{entity.PayloadType}', '{entity.Payload}', '{entity.Schedule}',
                '{entity.TimeZone}', {(int)entity.MisfirePolicy}, {entity.MaxCatchUp},
                {(entity.Enabled ? 1 : 0)},
                '{string.Join(";", entity.RetryIntervals.Select(ts => (long)ts.TotalMilliseconds))}',
                '{entity.NextRunAt:u}',
                {(entity.LastEnqueueAt.HasValue ? $"'{entity.LastEnqueueAt:u}'" : "NULL")},
                '{entity.CreatedAt:u}', '{sqlServerNow}'
            );
        """
    );
}
```

Note: SQL Server `MERGE` requires `WITH (HOLDLOCK)` on the target to prevent phantom inserts under concurrent load. The `GetDueJobs` analog uses `WITH (UPDLOCK, READPAST, ROWLOCK)` (line 24) as the hint style reference.

---

### `src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlDialect.cs`

**Analog:** `GetDueJobs` and `GetDueSchedules` in the same file (lines 17–74) for the `yyyy-MM-dd HH:mm:ss` timestamp format and the `` `backtick` `` quoting style (MySQL-specific).

#### UpsertScheduleAsync target pattern for MySQL

Replace the `NotImplementedException` stub (lines 76–80). MySQL uses `INSERT ... ON DUPLICATE KEY UPDATE ...`. Timestamp format is `yyyy-MM-dd HH:mm:ss` (see `GetDueJobs` line 19):

```csharp
public FormattableString UpsertScheduleAsync(AtomizerSchedule schedule)
{
    var entity = schedule.ToEntity();
    var mySqlNow = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
    var c = _schedules.Col;
    return FormattableStringFactory.Create(
        $"""
            INSERT INTO {_schedules.Table} (
                {c[nameof(AtomizerScheduleEntity.Id)]},
                {c[nameof(AtomizerScheduleEntity.JobKey)]},
                {c[nameof(AtomizerScheduleEntity.QueueKey)]},
                {c[nameof(AtomizerScheduleEntity.PayloadType)]},
                {c[nameof(AtomizerScheduleEntity.Payload)]},
                {c[nameof(AtomizerScheduleEntity.Schedule)]},
                {c[nameof(AtomizerScheduleEntity.TimeZone)]},
                {c[nameof(AtomizerScheduleEntity.MisfirePolicy)]},
                {c[nameof(AtomizerScheduleEntity.MaxCatchUp)]},
                {c[nameof(AtomizerScheduleEntity.Enabled)]},
                {c[nameof(AtomizerScheduleEntity.RetryIntervals)]},
                {c[nameof(AtomizerScheduleEntity.NextRunAt)]},
                {c[nameof(AtomizerScheduleEntity.LastEnqueueAt)]},
                {c[nameof(AtomizerScheduleEntity.CreatedAt)]},
                {c[nameof(AtomizerScheduleEntity.UpdatedAt)]}
            ) VALUES (
                '{entity.Id}', '{entity.JobKey}', '{entity.QueueKey}',
                '{entity.PayloadType}', '{entity.Payload}', '{entity.Schedule}',
                '{entity.TimeZone}', {(int)entity.MisfirePolicy}, {entity.MaxCatchUp},
                {(entity.Enabled ? "TRUE" : "FALSE")},
                '{string.Join(";", entity.RetryIntervals.Select(ts => (long)ts.TotalMilliseconds))}',
                '{entity.NextRunAt.ToString("yyyy-MM-dd HH:mm:ss")}',
                {(entity.LastEnqueueAt.HasValue ? $"'{entity.LastEnqueueAt.Value.ToString("yyyy-MM-dd HH:mm:ss")}'" : "NULL")},
                '{entity.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")}', '{mySqlNow}'
            )
            ON DUPLICATE KEY UPDATE
                {c[nameof(AtomizerScheduleEntity.QueueKey)]} = VALUES({c[nameof(AtomizerScheduleEntity.QueueKey)]}),
                {c[nameof(AtomizerScheduleEntity.PayloadType)]} = VALUES({c[nameof(AtomizerScheduleEntity.PayloadType)]}),
                {c[nameof(AtomizerScheduleEntity.Payload)]} = VALUES({c[nameof(AtomizerScheduleEntity.Payload)]}),
                {c[nameof(AtomizerScheduleEntity.Schedule)]} = VALUES({c[nameof(AtomizerScheduleEntity.Schedule)]}),
                {c[nameof(AtomizerScheduleEntity.TimeZone)]} = VALUES({c[nameof(AtomizerScheduleEntity.TimeZone)]}),
                {c[nameof(AtomizerScheduleEntity.MisfirePolicy)]} = VALUES({c[nameof(AtomizerScheduleEntity.MisfirePolicy)]}),
                {c[nameof(AtomizerScheduleEntity.MaxCatchUp)]} = VALUES({c[nameof(AtomizerScheduleEntity.MaxCatchUp)]}),
                {c[nameof(AtomizerScheduleEntity.Enabled)]} = VALUES({c[nameof(AtomizerScheduleEntity.Enabled)]}),
                {c[nameof(AtomizerScheduleEntity.RetryIntervals)]} = VALUES({c[nameof(AtomizerScheduleEntity.RetryIntervals)]}),
                {c[nameof(AtomizerScheduleEntity.NextRunAt)]} = VALUES({c[nameof(AtomizerScheduleEntity.NextRunAt)]}),
                {c[nameof(AtomizerScheduleEntity.UpdatedAt)]} = '{mySqlNow}';
        """
    );
}
```

Note: `ON DUPLICATE KEY UPDATE` triggers on any unique constraint violation, including the new unique index on `JobKey` added in D-07.

---

### `src/Atomizer.EntityFrameworkCore/Configurations/AtomizerScheduleEntityConfiguration.cs`

**Analog:** `AtomizerJobEntityConfiguration.cs` (lines 1–53) for the `builder.HasIndex(...)` call style. That file does not currently have a `HasIndex` call, but the EF Core API shape is identical to `HasIndex` documented in the EF Core docs. The `builder.HasKey(e => e.Id)` call on line 23 is the closest structural analog.

#### Existing Configure method to be amended (lines 20–54)

```csharp
public void Configure(EntityTypeBuilder<AtomizerScheduleEntity> builder)
{
    builder.ToTable("AtomizerSchedules", _schema);
    builder.HasKey(e => e.Id);
    builder.Property(job => job.Id).ValueGeneratedOnAdd();
    builder.Property(e => e.JobKey).IsRequired().HasMaxLength(512);
    // ... all existing properties ...
}
```

Append at the end of `Configure`, after all `Property` calls:

```csharp
builder.HasIndex(e => e.JobKey).IsUnique();
```

This single line is the complete change per D-07. The unique index is required for `ON CONFLICT (job_key)` / `MERGE ON job_key` SQL to be race-safe.

---

### `tests/Atomizer.EntityFrameworkCore.Tests/Fixtures/BaseDatabaseFixture.cs`

**Analog:** Self — one-line change on line 26.

#### Current line to replace (line 26)

```csharp
await DbContext.Database.MigrateAsync();
```

#### Replacement per D-08

```csharp
await DbContext.Database.EnsureCreatedAsync();
```

This is the complete change. `EnsureCreatedAsync` creates the schema from the current EF model (which now includes the `HasIndex(e => e.JobKey).IsUnique()` added in the configuration), without requiring migration history. Testcontainers always starts fresh, so no migration state exists to conflict.

---

### `tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/` (migration folders and design-time factories)

**Pattern:** Deletion only. No code to copy.

Files and directories to delete per D-08:

```
tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/MySql/Migrations/
tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/MySql/MySqlDesignTimeDbContextFactory.cs
tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/Postgres/Migrations/
tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/Postgres/PostgresDesignTimeDbContextFactory.cs
tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/Sqlite/Migrations/
tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/Sqlite/SqliteDesignTimeDbContextFactory.cs
tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/SqlServer/Migrations/
tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/SqlServer/SqlServerDesignTimeDbContextFactory.cs
```

The `*DbContext.cs` files (e.g. `PostgresDbContext.cs`, `SqliteDbContext.cs`) are retained — they are used by the fixtures at runtime.

---

## Shared Patterns

### Transaction scope: `DatabaseTransactionLeasingScope.StartTransaction`
**Source:** `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScope.cs` lines 65–84
**Apply to:** `EntityFrameworkCoreStorage.ExecuteInLeaseAsync` (both overloads)

```csharp
public static async Task<DatabaseTransactionLeasingScope> StartTransaction<TDbContext>(
    TDbContext dbContext,
    TimeSpan timeout,
    CancellationToken cancellationToken
)
    where TDbContext : DbContext
{
    try
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        return new DatabaseTransactionLeasingScope(
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cts.Token)
        );
    }
    catch
    {
        return new DatabaseTransactionLeasingScope(null);
    }
}
```

Key facts: any exception (including `OperationCanceledException` from timeout) returns `Acquired = false` — callers never see the exception. `DisposeAsync` commits on success, rolls back on any exception rethrown from within the `await using` block.

### Provider guard pattern
**Source:** `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` lines 84–116
**Apply to:** `UpsertScheduleAsync` refactor

Three-branch structure: (1) supported provider → dialect SQL, (2) unsupported + fallback flag → LINQ, (3) unsupported without flag → `NotSupportedException`. All three existing methods (`GetDueJobsAsync`, `ReleaseLeasedAsync`, `GetDueSchedulesAsync`) follow this shape.

### FormattableString dialect method shape
**Source:** `src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlDialect.cs` lines 60–76
**Apply to:** All three `UpsertScheduleAsync` dialect implementations

Every dialect method: (1) declares local `c = _schedules.Col` or `c = _jobs.Col`, (2) formats timestamp to string in provider-specific format, (3) returns `FormattableStringFactory.Create($"""...""")` with a raw string literal. The `FormattableString` return type — not `string` — is mandatory because EF Core's `FromSqlInterpolated`/`ExecuteSqlInterpolatedAsync` require it for parameterization safety.

### Error logging pattern
**Source:** `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` lines 66–72 and 147–151
**Apply to:** Any `catch (DbUpdateException ex)` block

```csharp
catch (DbUpdateException ex)
{
    _logger.LogError(ex, "Failed to update jobs");
}
```

Exception is the first argument to `LogError` per project logging conventions in CLAUDE.md.

---

## Column Name Resolution Note

The `EntityMap.Col` dictionary is keyed by C# property name (e.g. `nameof(AtomizerScheduleEntity.JobKey)`) and the value is the provider-escaped column name derived from the EF model at runtime (e.g. `"JobKey"` for SQL Server, `"\"JobKey\""` for PostgreSQL). The `ON CONFLICT (job_key)` SQL must use the bare (unquoted) column name — or the correctly-quoted version as returned by `EntityMap`. Check the Postgres migration snapshot at `tests/.../Postgres/Migrations/PostgresDbContextModelSnapshot.cs` to confirm the exact column name stored in the database before writing the PostgreSQL upsert SQL.

---

## No Analog Found

All files in this phase have close analogs in the existing codebase. No files require falling back to RESEARCH.md patterns.

---

## Metadata

**Analog search scope:**
- `src/Atomizer.EntityFrameworkCore/Storage/`
- `src/Atomizer.EntityFrameworkCore/Providers/`
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/`
- `src/Atomizer.EntityFrameworkCore/Configurations/`
- `tests/Atomizer.EntityFrameworkCore.Tests/Fixtures/`
- `tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/`

**Files scanned:** 18 source files + 5 test infrastructure files
**Pattern extraction date:** 2026-05-03
