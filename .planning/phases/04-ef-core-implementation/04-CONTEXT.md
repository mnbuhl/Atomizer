# Phase 4: EF Core Implementation - Context

**Gathered:** 2026-05-03
**Status:** Ready for planning

<domain>
## Phase Boundary

Implement `ExecuteInLeaseAsync` (both overloads) in `EntityFrameworkCoreStorage` — opens a `ReadCommitted` transaction spanning the full callback so `GetDueJobsAsync` row-locks and `UpdateJobsAsync` commit within the same transaction. Replace the check-then-insert `UpsertScheduleAsync` with native per-provider SQL via `ISqlDialect` (`ON CONFLICT` / `MERGE` / `ON DUPLICATE KEY UPDATE`). Add a unique index on `JobKey` in the schedules table via EF config + `EnsureCreatedAsync` (migrations deleted). SQLite / `AllowUnsafeProviderFallback` path keeps EF check-then-insert with a comment noting it is not race-safe and test-only.

</domain>

<decisions>
## Implementation Decisions

### Transaction Ownership

- **D-01:** `ExecuteInLeaseAsync` reuses `DatabaseTransactionLeasingScope.StartTransaction(...)` internally — proven commit/rollback/dispose logic, no duplication. Phase 5 deletes the class after storage no longer needs it.
- **D-02:** Lock timeout sourced from `EntityFrameworkCoreJobStorageOptions` — add a `LockTimeout` property (default 30s). `ExecuteInLeaseAsync` reads `_options.LockTimeout` when calling `StartTransaction`.
- **D-03:** If `StartTransaction` fails to acquire (returns `Acquired = false`, e.g. timeout), `ExecuteInLeaseAsync` returns silently — `Task.CompletedTask` (non-generic) or `Task.FromResult(default(TResult))` (generic). The poller skips the tick and retries next interval. No new exception type.

### Change Tracking Within Transaction

- **D-04:** Remove `AsNoTracking()` from `GetDueJobsAsync` when called inside a lease callback so EF tracks the fetched rows. `UpdateJobsAsync` keeps `UpdateRange()` everywhere (works regardless of tracking state). This is the simplest path with no behavioral change to `UpdateJobsAsync` for callers outside the lease.

### Upsert: Native SQL for Supported Providers

- **D-05:** `ISqlDialect.UpsertScheduleAsync` is implemented in all three dialects:
  - `PostgreSqlDialect`: `INSERT ... ON CONFLICT (job_key) DO UPDATE SET ...`
  - `SqlServerDialect`: `MERGE ... USING ... WHEN MATCHED THEN UPDATE WHEN NOT MATCHED THEN INSERT`
  - `MySqlDialect`: `INSERT ... ON DUPLICATE KEY UPDATE ...`
- **D-06:** `EntityFrameworkCoreStorage.UpsertScheduleAsync` routes to `_providerCache.Dialect.UpsertScheduleAsync(schedule)` for supported providers (same pattern as `GetDueJobsAsync`). For unsupported/fallback providers, the existing EF check-then-insert path is kept with a comment:
  ```csharp
  // Not race-safe — only used for test-only providers (SQLite) via AllowUnsafeProviderFallback
  ```

### Unique Index on JobKey

- **D-07:** Add `.HasIndex(e => e.JobKey).IsUnique()` to `AtomizerScheduleEntityConfiguration`. This is required by `ON CONFLICT (job_key)` / `MERGE ON job_key` SQL. No unique index exists today.
- **D-08:** Migrations are deleted entirely from the test project. `BaseDatabaseFixture.InitializeAsync` is updated to call `EnsureCreatedAsync()` instead of `MigrateAsync()`. Testcontainers always starts fresh — no migration history needed. All migration folders and `DesignTimeDbContextFactory` classes in `tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/` are removed.

### Claude's Discretion

- Exact property name for `LockTimeout` on `EntityFrameworkCoreJobStorageOptions` (e.g. `LockAcquisitionTimeout`, `TransactionTimeout` — either is fine).
- Whether `AsNoTracking()` is removed only inside the `ExecuteInLeaseAsync` callback path or globally from `GetDueJobsAsync` (simpler to remove globally since the method doesn't cache entities).
- Exact column name used in `ON CONFLICT (job_key)` — must match the actual column name from `EntityMap` / EF config (`HasColumnName` if overridden, otherwise convention-derived).
- XML documentation wording for `LockTimeout` property.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Requirements Governing This Phase
- `.planning/REQUIREMENTS.md` — Requirements ACQR-01, ACQR-02, ACQR-03, UPSRT-01, UPSRT-02, UPSRT-03, UPSRT-04 govern Phase 4
- `.planning/ROADMAP.md` — Phase 4 success criteria (7 items) are the acceptance test

### EF Core Storage Files Being Changed
- `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` — Implement `ExecuteInLeaseAsync` overloads; update `UpsertScheduleAsync` to route to dialect; remove `AsNoTracking()` from `GetDueJobsAsync`
- `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScope.cs` — Reused by `ExecuteInLeaseAsync` (read only; Phase 5 deletes it)
- `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreJobStorageOptions.cs` — Add `LockTimeout` property (default 30s)

### Dialect Files Being Changed
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlDialect.cs` — Implement `UpsertScheduleAsync` with `INSERT ... ON CONFLICT`
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/SqlServerDialect.cs` — Implement `UpsertScheduleAsync` with `MERGE`
- `src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlDialect.cs` — Implement `UpsertScheduleAsync` with `INSERT ... ON DUPLICATE KEY UPDATE`
- `src/Atomizer.EntityFrameworkCore/Providers/ISqlDialect.cs` — Interface already declares `UpsertScheduleAsync`; no change needed

### EF Config Being Changed
- `src/Atomizer.EntityFrameworkCore/Configurations/AtomizerScheduleEntityConfiguration.cs` — Add `.HasIndex(e => e.JobKey).IsUnique()`

### Test Infrastructure Being Changed
- `tests/Atomizer.EntityFrameworkCore.Tests/Fixtures/BaseDatabaseFixture.cs` — Replace `MigrateAsync()` with `EnsureCreatedAsync()`
- `tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/` — Delete all Migrations/ folders and DesignTimeDbContextFactory classes

### Prior Phase Context (locked decisions)
- `.planning/phases/01-leasing-abstraction/01-CONTEXT.md` — `ExecuteInLeaseAsync` contract shape (generic + non-generic overloads, callback signature)
- `.planning/phases/03-sql-dialect-strategy/03-CONTEXT.md` — `ISqlDialect` shape (4 methods), `RelationalProviderCache.Dialect` property, `internal sealed` visibility

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `DatabaseTransactionLeasingScope.StartTransaction<TDbContext>(dbContext, timeout, ct)` — factory method already handles `BeginTransactionAsync`, `ReadCommitted` isolation, timeout via linked CTS, and returns `Acquired = false` on failure. Reuse directly in `ExecuteInLeaseAsync`.
- `DatabaseTransactionLeasingScope.DisposeAsync()` — commits on success, rolls back on exception, disposes the transaction. Wire via `await using` inside `ExecuteInLeaseAsync`.
- `_providerCache.Dialect.GetDueJobs(...)` / `GetDueSchedules(...)` / `ReleaseLeasedJobs(...)` call sites — established pattern in `EntityFrameworkCoreStorage`; `UpsertScheduleAsync` follows the same `if (_providerCache is { IsSupportedProvider: true, Dialect: not null })` guard.
- `EntityMap` (jobs and schedules) — already built in `RelationalProviderCache`, available to dialect constructors. Upsert SQL needs schedule column names from `_schedules.Col`.

### Established Patterns
- `if (_providerCache is { IsSupportedProvider: true, Dialect: not null })` pattern — guards both the raw SQL path and the fallback LINQ path in `GetDueJobsAsync` and `GetDueSchedulesAsync`. Apply the same pattern in `UpsertScheduleAsync`.
- `FromSqlInterpolated` for SELECT queries (returns entities), `ExecuteSqlInterpolatedAsync` for non-SELECT (returns row count). Upsert is a non-SELECT → use `ExecuteSqlInterpolatedAsync`.
- `internal sealed class` for all storage/dialect implementations.
- `FormattableString` return type on all `ISqlDialect` methods — dialect produces the interpolated SQL; storage calls it via EF.

### Integration Points
- `ExecuteInLeaseAsync` must use the same `_dbContext` instance as the caller's subsequent `GetDueJobsAsync` / `UpdateJobsAsync` calls — the transaction is on the `DbContext`'s connection. The callback receives the same `_dbContext` via closure (no parameter needed since storage is a single instance per scope).
- `QueuePoller` and `SchedulePoller` call `storage.ExecuteInLeaseAsync(queue, callback, ct)` — they are the only callers. No changes needed to the pollers.
- `GetDueJobsAsync` currently called outside `ExecuteInLeaseAsync` by `AllowUnsafeProviderFallback` path — no transaction there. Removing `AsNoTracking()` from that method is safe since `DbContext` is `Scoped` and fresh per call.

</code_context>

<specifics>
## Specific Ideas

- `ExecuteInLeaseAsync<TResult>` implementation sketch:
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
  ```
  `DisposeAsync()` commits on success or rolls back on exception automatically.

- Upsert SQL for PostgreSQL (column names via `_schedules.Col`):
  ```sql
  INSERT INTO {_schedules.Table} (...) VALUES (...)
  ON CONFLICT (job_key) DO UPDATE SET
      payload = EXCLUDED.payload,
      next_run_at = EXCLUDED.next_run_at,
      ...
  ```

- `BaseDatabaseFixture` change: replace `await DbContext.Database.MigrateAsync()` with `await DbContext.Database.EnsureCreatedAsync()`.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 4-EF Core Implementation*
*Context gathered: 2026-05-03*
