# Codebase Concerns

**Analysis Date:** 2026-05-03

## Tech Debt

**Idempotency key enforcement (EF Core):**
- Issue: `InsertAsync` enforces idempotency with a read-before-insert pattern instead of a unique index. The `@todo: make idempotency key unique with index` comment flags this.
- Files: `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:37-55`
- Impact: Two concurrent enqueues with the same `IdempotencyKey` can both pass the existence check and both insert, producing duplicate jobs. The framework-level idempotency guarantee silently degrades under concurrency.
- Fix approach: Add a unique index on `AtomizerJobEntity.IdempotencyKey` (filtered index on non-null values for SQL Server / partial index on PostgreSQL); catch the resulting `DbUpdateException` / unique-violation and return the existing job's ID. Remove the read-before-write branch.

**Schedule upsert race condition:**
- Issue: `UpsertScheduleAsync` reads-then-writes without optimistic concurrency control. Inline comment states: "Might fail due to race conditions in a distributed setup. Look into optimistic concurrency control later."
- Files: `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:155-190`
- Impact: In multi-instance deployments, two instances calling `ScheduleRecurringAsync` for the same `JobKey` at startup can both miss the existing row and both issue an `Add`, or one's `Update` overwrites the other's changes. Failures are swallowed (logged only), so the caller receives an `entity.Id` that may never have been persisted.
- Fix approach: Add unique index on `AtomizerScheduleEntity.JobKey`; use provider-specific `MERGE` / `INSERT ... ON CONFLICT` / `INSERT ... ON DUPLICATE KEY UPDATE`; add a `RowVersion`/`xmin` concurrency token; surface failures to the caller instead of swallowing `DbUpdateException`.

**Swallowed persistence failures:**
- Issue: `UpdateJobsAsync`, `UpdateSchedulesAsync`, `ReleaseLeasedAsync`, and `UpsertScheduleAsync` all catch `DbUpdateException` and log-but-return. Callers receive a "success" response for what was actually a persistence failure.
- Files: `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:69-73,144-153,177-187,194-202`
- Impact: Leased-job state changes (completion, retry, failure) can silently fail to persist, leaving jobs stuck in `Processing` until visibility timeout expires. Release-on-shutdown silently fails, extending job reappearance delays.
- Fix approach: Let `DbUpdateException` propagate; only catch it for idempotency-specific conflict handling. Add structured result types (`StorageResult<T>`) so callers can distinguish transient failures.

**Reflection-based handler dispatch:**
- Issue: `DefaultJobDispatcher.DispatchAsync` uses `handlerType.GetMethod("HandleAsync", ...)` and `method.Invoke(...)` per job.
- Files: `src/Atomizer/Core/DefaultJobDispatcher.cs:72-84`
- Impact: Per-job reflection overhead (method lookup + invocation); `TargetInvocationException` unwrap adds complexity. No AOT / trim-safe attributes; will break `PublishAot` and trimming scenarios without `[DynamicallyAccessedMembers]` on `handlerType`.
- Fix approach: Cache compiled delegates per `(handlerType, payloadType)` using `Expression.Compile()` or a source-generated dispatcher. Annotate `handlerType` parameters with `DynamicallyAccessedMembersAttribute` for trim-safety.

**`netstandard2.0` multi-target constraints:**
- Issue: CLAUDE.md references `netstandard2.1` but `Atomizer.csproj` actually targets `netstandard2.0;net8.0;net10.0`. The lowest target forces `#if NETCOREAPP3_0_OR_GREATER` guards for `IAsyncDisposable` / `await using`.
- Files: `src/Atomizer/Atomizer.csproj:3`, `src/Atomizer/Processing/QueuePoller.cs:59-71`
- Impact: Conditional compilation complexity; synchronous `Dispose()` path on `netstandard2.0` cannot await transaction commit/rollback (applies to consumers on .NET Framework / older runtimes); CLAUDE.md is out of date and will mislead future contributors.
- Fix approach: Update CLAUDE.md to reflect the actual target framework set. Consider whether `netstandard2.0` adds meaningful reach versus dropping to `netstandard2.1` / `net6.0` minimums.

**Scheduler `UpsertScheduleAsync` ID return semantics:**
- Issue: Returns `entity.Id` even when `SaveChangesAsync` threw and was caught. The returned ID may not correspond to a persisted row.
- Files: `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:173-189`
- Impact: `AtomizerClient.ScheduleRecurringAsync` can appear to succeed while no schedule exists.
- Fix approach: Return `Guid?` or `StorageResult<Guid>`; only return the ID on confirmed `SaveChangesAsync` completion.

## Known Bugs

**In-memory lock timeout can release an unheld semaphore:**
- Symptoms: On an expired lock, the code calls `semaphore.Release()` without owning the slot, then re-attempts `WaitAsync`. If another task re-acquired between timeout detection and release, the count jumps to 2 and two holders can execute concurrently.
- Files: `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs:58-77`
- Trigger: Poller for queue A stalls past `VisibilityTimeout`; a second poller attempt occurs; the first eventually completes and calls `Dispose` → `semaphore.Release()` raising the count above 1 briefly. Concurrent `WaitAsync(TimeSpan.Zero)` calls both succeed.
- Workaround: The static `Semaphores` dictionary is process-scoped so this only affects the in-memory backend, which is not intended for production multi-instance use.

**Stale `acquiredTimestamp` in `Semaphores` tuple:**
- Symptoms: `Semaphores.GetOrAdd(key, (new SemaphoreSlim(1, 1), acquiredAt))` stores the timestamp from the first caller forever. Subsequent callers compare against an ever-growing-stale `acquiredTimestamp`, so after the first acquisition the "lock has timed out" check will always be true.
- Files: `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs:58-61`
- Trigger: Any second+ acquisition of the same queue key after the in-memory factory has been used once.
- Workaround: None; the lock-timeout recovery path is broadly unreachable for legitimate cases and trivially reachable for misfire.
- Fix approach: Store the timestamp in the scope itself; update on acquisition; use an interlocked pattern rather than tuple state.

**Static process-wide semaphore dictionary:**
- Symptoms: `Semaphores` is `static readonly ConcurrentDictionary<...>`. Test isolation and multi-host scenarios share state.
- Files: `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs:10`
- Trigger: Two `IHost` instances in the same test process will share per-queue locks.
- Fix approach: Make the dictionary instance-scoped on the factory.

**`RelationalProviderCache.Instances` is static across all `DbContext`s:**
- Symptoms: Cache is keyed only by `DatabaseProvider`, not by `DbContext` type or connection string. Two `DbContext`s with different schemas / table mappings but the same provider share one `EntityMap`.
- Files: `src/Atomizer.EntityFrameworkCore/Providers/RelationalProviderCache.cs:29-55`
- Trigger: Any application hosting two Atomizer configurations with different `TDbContext`s on the same provider.
- Fix approach: Key the cache on `(DatabaseProvider, Type dbContextType)` or on the `IModel` reference itself.

**`leasingScope.Acquired` ignored on error path:**
- Symptoms: `InMemoryLeasingScopeFactory.CreateScopeAsync` calls `.Result` on the task synchronously for logging, which blocks the caller and can deadlock in single-threaded sync contexts.
- Files: `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs:28-37`
- Trigger: Call from a `SynchronizationContext` that marshals continuations.
- Fix approach: `await` the scope instead of reading `.Result`.

## Security Considerations

**Raw SQL string interpolation with `FormattableString`:**
- Risk: SQL providers construct queries with `FormattableStringFactory.Create($"...")` and literal string interpolation for column names, status enum values, and timestamps. While `QueueKey` values and lease tokens are inlined as `'{queueKey}'` string literals rather than parameters, reliance on `FromSqlInterpolated` for parameterization is bypassed — the interpolated values become part of the SQL text.
- Files: `src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlProvider.cs`, `src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlProvider.cs`, `src/Atomizer.EntityFrameworkCore/Providers/Sql/SqlServerProvider.cs`
- Current mitigation: `QueueKey`, `LeaseToken`, and `JobKey` have validation in their value-object constructors that limits lengths and rejects empty values. However none reject SQL metacharacters (apostrophe, semicolon).
- Recommendations: Switch to properly-parameterized `FromSqlInterpolated` (let EF Core handle parameters) or use `FromSqlRaw(string, params object[])` with explicit parameters. At minimum, add explicit apostrophe/metacharacter validation on `QueueKey.Key` and `JobKey` value objects. Audit call sites: `EntityFrameworkCoreStorage.GetDueJobsAsync` passes the interpolated SQL through `FromSqlInterpolated` which does not re-parameterize an already-built `FormattableString` whose holes were inlined as literals.

**Payload deserialization uses `System.Text.Json` with runtime `Type`:**
- Risk: `DefaultJobSerializer` deserializes arbitrary `Type` instances resolved from stored type names. If stored `PayloadType` is mutable by untrusted actors, arbitrary type construction is possible.
- Files: `src/Atomizer/Core/DefaultJobSerializer.cs`, `src/Atomizer/Core/DefaultJobTypeResolver.cs`
- Current mitigation: `IAtomizerJobTypeResolver` presumably restricts to registered handler payload types (resolver throws `JobResolverException` if unregistered).
- Recommendations: Document the trust boundary (the job storage backend is trusted); ensure `DefaultJobTypeResolver.Resolve` uses only the scanned `AddHandlersFrom<T>()` registrations, never full-assembly reflection by name.

**Lease tokens are process-identifying:**
- Risk: `LeaseToken` format is `{InstanceId}:*:{QueueKey}:*:{Guid}`. If logs are shipped externally, the instance identifier and queue topology leak.
- Files: `src/Atomizer/Processing/QueuePump.cs:57`
- Current mitigation: None.
- Recommendations: Consider hashing or shortening for log output if sensitive.

## Performance Bottlenecks

**In-memory `GetDueJobsAsync` is O(queue size) per poll:**
- Problem: Iterates the full queue `HashSet<Guid>` and materializes all jobs each poll to filter/sort. No secondary index on `ScheduledAt` or `Status`.
- Files: `src/Atomizer/Storage/InMemoryStorage.cs:93-104`
- Cause: Filter/`OrderBy` runs over every job in the queue, not just due ones.
- Improvement path: Maintain a `SortedSet<AtomizerJob>` or priority-queue keyed by `ScheduledAt`; only relevant for stress scenarios since `InMemory` is test/dev only.

**EF Core `UpdateRange` writes all columns:**
- Problem: `UpdateJobsAsync` and `UpdateSchedulesAsync` call `UpdateRange` with entities converted from domain objects. EF treats every column as modified, producing full-row UPDATEs even when only `Status` / `VisibleAt` changed.
- Files: `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:62-73,192-202`
- Cause: Detached-entity update pattern; no change tracking on the domain model.
- Improvement path: Use `ExecuteUpdate` (EF 7+) for targeted column updates, or attach the entity and mark only mutated properties as modified. Requires gating behind `net8.0+` target.

**Poller tick interval coupled to storage check interval:**
- Problem: `TickInterval` is hardcoded to 1 second in `QueueOptions` (`private set`) independent of `StorageCheckInterval` (default 15s). Poller wakes every second just to compute "am I due yet."
- Files: `src/Atomizer/Configuration/QueueOptions.cs:37`, `src/Atomizer/Processing/QueuePoller.cs:139`
- Cause: Busy-loop design avoids complex scheduling logic but wastes scheduler slots.
- Improvement path: `Task.Delay(storageCheckInterval - elapsed)` instead of fixed 1-second ticks; or use a `PeriodicTimer`.

**Channel capacity sizing:**
- Problem: `Channel.CreateBounded(DegreeOfParallelism * BatchSize)` with `FullMode = Wait` means poller blocks when the channel is full. Poller checks `itemsInChannel < queue.DegreeOfParallelism` before leasing, but post-lease `WriteAsync` can still block if workers stall.
- Files: `src/Atomizer/Processing/QueuePump.cs:48-55`, `src/Atomizer/Processing/QueuePoller.cs:117`
- Cause: Mismatched upstream (poll-gate) and downstream (write) capacity thresholds.
- Improvement path: Align the gate check with channel capacity, or size the channel proportionally larger and let the gate be the rate limiter.

**Schedule enqueue is not batched:**
- Problem: `ScheduleProcessor` calls `storage.InsertAsync` per occurrence. For a `CatchUp` policy with `MaxCatchUp = 5`, that is 5 round-trips per schedule.
- Files: `src/Atomizer/Scheduling/` (per architecture notes)
- Cause: No `InsertManyAsync` on `IAtomizerStorage`.
- Improvement path: Add batch insert to `IAtomizerStorage`; EF provider uses `AddRange` + single `SaveChangesAsync`.

## Fragile Areas

**Distributed leasing via `ReadCommitted` transaction + `SKIP LOCKED`:**
- Files: `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScope.cs`, `src/Atomizer.EntityFrameworkCore/Providers/Sql/*.cs`
- Why fragile: `StartTransaction` catches all exceptions and returns an un-acquired scope silently. A connection failure, timeout, or deadlock looks identical to "locks held by others." The poller logs "Failed to acquire processing scope" with no detail.
- Safe modification: Any change to the raw SQL must be applied to all three providers simultaneously (Postgres `FOR NO KEY UPDATE SKIP LOCKED`, MySQL `FOR UPDATE SKIP LOCKED`, SQL Server `WITH (UPDLOCK, READPAST, ROWLOCK)`). The three files diverge in lock hints and must stay semantically aligned.
- Test coverage: `tests/Atomizer.EntityFrameworkCore.Tests/` uses Testcontainers per provider. Verify all three providers run in CI on every change.

**Misfire policy logic:**
- Files: `src/Atomizer/Models/AtomizerSchedule.cs:62-91`
- Why fragile: `GetOccurrences` interprets three policies differently. `CatchUp` uses `LastEnqueueAt ?? CreatedAt` as the start window; a never-run schedule with a `CreatedAt` far in the past will produce `MaxCatchUp` catch-up jobs that may not be desirable.
- Safe modification: Do not change the `LastEnqueueAt` default without considering schedules whose `CreatedAt` is historical (e.g., imported from another system).
- Test coverage: `tests/Atomizer.Tests/Scheduling/` exists; verify each misfire policy × (new schedule, resumed schedule, paused then re-enabled) case.

**Idempotency key format for recurring jobs:**
- Files: `src/Atomizer/Scheduling/ScheduleProcessor.cs` (per CLAUDE.md)
- Why fragile: Key format `{JobKey}:*:{occurrence:O}` embeds the occurrence timestamp. Clock skew between instances, timezone changes on the schedule, or DST transitions could produce duplicates or miss duplicates.
- Safe modification: Never change the format without a storage migration path (existing keys in the DB would stop matching new keys).

**Reflection cache absence in dispatcher:**
- Files: `src/Atomizer/Core/DefaultJobDispatcher.cs:72`
- Why fragile: `GetMethod("HandleAsync", ...)` is called per dispatch. If `IAtomizerJob<T>` ever grows multiple `HandleAsync` overloads, the string lookup will start throwing `AmbiguousMatchException`.
- Safe modification: Pin the interface contract by adding a strict overload check, or switch to `typeof(IAtomizerJob<>).MakeGenericType(payloadType).GetMethod(...)` directly from the interface.

**Shutdown job release races with in-flight processing:**
- Files: `src/Atomizer/Processing/QueuePump.cs:122-143`
- Why fragile: On graceful-shutdown timeout, `_executionCts.Cancel()` fires and then `ReleaseLeasedAsync` runs concurrently with potentially-still-running workers that may still be calling `UpdateJobsAsync` to mark jobs completed/failed. Last-writer-wins determines whether the job is released or completed.
- Safe modification: Await workers after `_executionCts.Cancel()` (with a second, short deadline) before calling `ReleaseLeasedAsync`.
- Test coverage: Unclear; would need a test where the handler blocks and is cancelled mid-update.

**EF update concurrency on unleased jobs:**
- Files: `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:62-73`
- Why fragile: `UpdateJobsAsync` does not filter by lease token. A stale update (e.g. from a worker whose lease expired and the job was re-leased by another host) will clobber the new lessee's state.
- Safe modification: Add `WHERE Id = @id AND LeaseToken = @leaseToken` semantics, either via `ExecuteUpdate` or a `RowVersion` concurrency token on `AtomizerJobEntity`.

## Scaling Limits

**In-memory storage:**
- Current capacity: `AmountOfJobsToRetainInMemory` default 100 terminal jobs. Active (non-terminal) jobs unbounded.
- Limit: Memory pressure as active job count grows. No eviction for pending/processing jobs.
- Scaling path: Use EF Core storage for anything beyond single-process development.

**Poller `BatchSize` default of 10:**
- Current capacity: 10 jobs per `StorageCheckInterval` (15s) = ~40 jobs/minute/queue without the worker count limit.
- Limit: At peak the system caps at `BatchSize / StorageCheckInterval` enqueues-per-queue regardless of available worker capacity.
- Scaling path: Tune `BatchSize` and `DegreeOfParallelism` per queue; the poller only re-polls when the channel drops below `DegreeOfParallelism`.

**Single `DbContext` per storage instance:**
- Current capacity: `EntityFrameworkCoreStorage` takes one `TDbContext` instance. Storage is resolved per-scope, so one DB connection per poll cycle.
- Limit: `DbContext` is not thread-safe. Concurrent calls from multiple workers will fault.
- Scaling path: Pattern relies on scoped DI; verify workers never share a storage instance.

## Dependencies at Risk

**Cronos for cron parsing:**
- Risk: Single-maintainer package; last major version behavior depends on 5-part vs 6-part interpretation.
- Impact: Schedule parsing differences across Cronos versions.
- Migration plan: Isolated inside `Schedule` value object and `AtomizerSchedule.CronExpression`. Swap to `NCrontab` or `Quartz` expression parser possible via single-file change.

**Pomelo.EntityFrameworkCore.MySql:**
- Risk: Pomelo is a community MySQL provider; MySQL.EntityFrameworkCore (Oracle's) is also detected.
- Impact: MySQL support covers two different client libraries with subtly different SQL behaviors.
- Migration plan: Integration-test coverage on both; currently `RelationalProviderCache.DetectProvider` maps both to `DatabaseProvider.MySql`.

**Oracle support removed:**
- Risk: Commit `780267c` removed first-party Oracle. The `DatabaseProvider.Oracle` enum value and detection remain.
- Impact: Users running Oracle hit "not supported" unless they set `AllowUnsafeProviderFallback = true` (which uses LINQ fallback with no row-level locking).
- Files: `src/Atomizer.EntityFrameworkCore/Providers/RelationalProviderCache.cs:79-86`
- Migration plan: Either fully remove the Oracle enum value and detection, or re-add a provider implementation with `FOR UPDATE SKIP LOCKED` (supported on Oracle 12c+).

## Missing Critical Features

**No observability surface:**
- Problem: No `ActivitySource` for OpenTelemetry tracing; no metrics emission (`Meter` for job counts, lease durations, retries).
- Blocks: Production observability; users cannot see queue depth, processing latency, or failure rates without tailing logs.

**No dead-letter queue:**
- Problem: Jobs that exhaust `RetryStrategy.MaxAttempts` transition to `Failed` status but remain in the primary queue table. No separate DLQ surface or re-drive tooling.
- Blocks: Operator remediation workflows. To retry a failed job requires direct DB manipulation.

**No job cancellation API:**
- Problem: `IAtomizerClient` exposes `EnqueueAsync`, `ScheduleAsync`, `ScheduleRecurringAsync` but no `CancelAsync(Guid jobId)` or `DisableScheduleAsync(JobKey)`.
- Blocks: Ability to stop a pending or scheduled job without direct storage mutation.

**No `InsertManyAsync` / batch enqueue:**
- Problem: `IAtomizerStorage.InsertAsync` is single-job. Batch scenarios (scheduler catch-up, bulk enqueue) pay N round-trips.
- Blocks: High-throughput scenarios; scheduler misfire catch-up scales linearly with `MaxCatchUp`.

**No health-check integration:**
- Problem: No `IHealthCheck` implementation to expose storage reachability or queue processing status.
- Blocks: Standard ASP.NET Core health endpoints cannot report Atomizer state.

**No EF Core migrations shipped:**
- Problem: `AddAtomizerEntities(schema: "atomizer")` configures the model but consumers must author their own migrations for the three entity tables.
- Blocks: Onboarding friction; schema drift between consumers.

## Test Coverage Gaps

**Concurrency tests for `UpsertScheduleAsync`:**
- What's not tested: Two instances upserting the same `JobKey` simultaneously.
- Files: `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:155-190`
- Risk: The known race-condition `@todo` could be validated or reproduced with a concurrent integration test.
- Priority: High

**Idempotency-under-concurrency tests:**
- What's not tested: Two concurrent `InsertAsync` calls with the same `IdempotencyKey`.
- Files: `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:33-60`
- Risk: Duplicate-insert scenario is currently undetectable without a test.
- Priority: High

**`AllowUnsafeProviderFallback` path:**
- What's not tested: LINQ fallback on an unsupported provider (Sqlite, Oracle).
- Files: `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:93-109,224-231`
- Risk: Race conditions on the LINQ path (no row locking) are not exercised. Two pollers can lease the same job.
- Priority: Medium — feature is opt-in and labelled "unsafe."

**In-memory lock timeout recovery:**
- What's not tested: Verifying `InMemoryLeasingScopeFactory` releases a stale lock correctly and the stale `acquiredTimestamp` bug.
- Files: `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs:51-77`
- Risk: Known-broken behavior (stale timestamp) exists without a regression test.
- Priority: Medium

**Shutdown-race tests:**
- What's not tested: Worker completes job mid-`ReleaseLeasedAsync`; handler ignores cancellation and writes post-grace-period.
- Files: `src/Atomizer/Processing/QueuePump.cs:89-148`
- Risk: Graceful-shutdown correctness is the central reliability claim of the library.
- Priority: High

**Multi-provider SQL parity tests:**
- What's not tested: Cross-provider equivalence — that PostgreSQL, MySQL, and SQL Server produce identical ordering, skip-locked semantics, and respect the same `ScheduledAt`/`VisibleAt` predicates.
- Files: `src/Atomizer.EntityFrameworkCore/Providers/Sql/*.cs`
- Risk: Divergence across providers can produce subtle correctness drift (e.g., SQL Server `WITH (READPAST)` skips committed rows differently from `SKIP LOCKED`).
- Priority: Medium

**Reflection-dispatch edge cases:**
- What's not tested: Handler with multiple `HandleAsync` overloads; `TargetInvocationException` wrapping a rethrown `OperationCanceledException`; payload type with non-default constructor.
- Files: `src/Atomizer/Core/DefaultJobDispatcher.cs`
- Risk: Handler authors hit surprising behavior.
- Priority: Low

**`RelationalProviderCache` cross-`DbContext` bleed:**
- What's not tested: Two different `DbContext` types using the same provider share cache state.
- Files: `src/Atomizer.EntityFrameworkCore/Providers/RelationalProviderCache.cs:29-55`
- Risk: Entity-map collision when schema/table names differ.
- Priority: Medium

**Test utilities suggest healthy unit coverage:** `tests/Atomizer.Tests.Utilities/` provides `FakeDataFactory`, `NonPublicSpy`, `TestableLogger`, and sample jobs; `tests/Atomizer.EntityFrameworkCore.Tests/Fixtures/` runs Testcontainers for PostgreSQL, SQL Server, MySQL, plus SQLite without a container. Structure is solid; gaps above are targeted scenarios rather than framework omissions.

---

*Concerns audit: 2026-05-03*
