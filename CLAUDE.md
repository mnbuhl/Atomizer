# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run tests for a specific project
dotnet test tests/Atomizer.Tests/Atomizer.Tests.csproj
dotnet test tests/Atomizer.EntityFrameworkCore.Tests/Atomizer.EntityFrameworkCore.Tests.csproj

# Run a single test by name filter
dotnet test --filter "FullyQualifiedName~MethodName_WhenScenario_ShouldBehavior"

# Format code
dotnet csharpier .
```

## Architecture

Atomizer is a background job scheduling and queueing framework for ASP.NET Core, structured as two NuGet packages:

- **`src/Atomizer`** — Core library targeting `netstandard2.1`/`net8.0`. Contains all abstractions, models, processing pipeline, and in-memory storage.
- **`src/Atomizer.EntityFrameworkCore`** — EF Core storage backend targeting `net6.0`/`net8.0`.

### Public API (`IAtomizerClient`)

Three operations available via `IAtomizerClient`:
- `EnqueueAsync<TPayload>` — enqueue immediately (with optional `EnqueueOptions`: queue, idempotency key, retry strategy).
- `ScheduleAsync<TPayload>` — enqueue at a future `DateTimeOffset`.
- `ScheduleRecurringAsync<TPayload>` — upsert a recurring schedule by `JobKey` (with `RecurringOptions`: misfire policy, catch-up, timezone).

### Key Abstractions (`src/Atomizer/Abstractions/`)

- `IAtomizerStorage` — Storage contract: `InsertAsync`, `UpdateJobsAsync`, `GetDueJobsAsync`, `ReleaseLeasedAsync`, `UpsertScheduleAsync`, `UpdateSchedulesAsync`, `GetDueSchedulesAsync`.
- `IAtomizerLeasingScopeFactory` / `IAtomizerLeasingScope` — Distributed locking abstraction used during job polling. `Acquired` indicates whether the scope was obtained. EF Core implements this as a `ReadCommitted` database transaction (`DatabaseTransactionLeasingScope`); in-memory uses per-queue `SemaphoreSlim` with timeout detection (`InMemoryLeasingScopeFactory`).
- `IAtomizerJob<TPayload>` — Consumer-implemented handler interface with `HandleAsync(TPayload, JobContext)`.
- `IAtomizerJobSerializer` / `IAtomizerJobTypeResolver` / `IAtomizerJobDispatcher` — Internal serialization, type resolution, and handler dispatch chain.

### Job Lifecycle

```
Pending → (leased) → Processing → Completed
                                 → Pending (reschedule on retry)
                                 → Failed (retries exhausted)
```

`AtomizerJob` state transitions are enforced by domain methods: `Lease`, `Attempt`, `MarkAsCompleted`, `MarkAsFailed`, `Reschedule`, `Release`. Status is stored as an integer enum (`Pending=1`, `Processing=2`, `Completed=3`, `Failed=4`).

### Processing Pipeline (`src/Atomizer/Processing/`)

```
AtomizerQueueService (IHostedService)
  └── QueueCoordinator — starts one QueuePump per configured queue
        └── QueuePump — per-queue: owns a BoundedChannel<AtomizerJob>
              │         capacity = DegreeOfParallelism × BatchSize
              ├── QueuePoller (Task) — polls storage on StorageCheckInterval;
              │   skips if channel has ≥ DegreeOfParallelism items already
              └── JobWorker(s) [DegreeOfParallelism tasks]
                    └── JobProcessor — calls IAtomizerJobDispatcher
                          └── DefaultJobDispatcher — resolves IAtomizerJob<T>
                                from a new DI scope, invokes via reflection
```

Two cancellation tokens flow through the pipeline:
- `ioToken` — cancelled first; stops the poller and signals workers to stop reading.
- `executionToken` — cancelled only if graceful shutdown deadline passes; interrupts running handlers.

On shutdown, `QueuePump.StopAsync` drains workers up to the grace period, then calls `ReleaseLeasedAsync` to return any in-flight jobs to `Pending` so they reappear after visibility timeout.

### Scheduling (`src/Atomizer/Scheduling/`)

`AtomizerSchedulerService` (IHostedService) → `SchedulePoller` polls storage for due `AtomizerSchedule` records → `ScheduleProcessor` calls `AtomizerSchedule.GetOccurrences()` which applies the `MisfirePolicy` (Ignore / ExecuteNow / CatchUp) and enqueues one `AtomizerJob` per occurrence using an idempotency key `{JobKey}:*:{occurrence:O}`. After processing, `UpdateNextOccurence` advances `NextRunAt` via Cronos.

`Schedule` value object wraps a 6-part cron (seconds-level). Predefined constants: `EverySecond`, `EveryMinute`, `Hourly`, `Daily`, `Weekly`, `Monthly`. Custom via `Schedule.Cron("...")` accepting 5- or 6-part expressions.

`RetryStrategy` is a value object with three factory methods:
- `RetryStrategy.Fixed(delay, maxAttempts, jitter)` — constant intervals ±20% jitter.
- `RetryStrategy.Intervals(IEnumerable<TimeSpan>)` — explicit per-attempt delays.
- `RetryStrategy.Exponential(initialInterval, maxAttempts, exponent, maxInterval, jitter)`.
- `RetryStrategy.None` — single attempt, no retry.
- Default: `Fixed(15s, 3, jitter: true)`.

### Storage Backends

**InMemory** (`src/Atomizer/Storage/`):
- `ConcurrentDictionary<Guid, AtomizerJob>` for jobs, `Dictionary<QueueKey, HashSet<Guid>>` for queue indexes.
- Evicts completed/failed jobs keeping the most recent `AmountOfJobsToRetainInMemory` (default 100) terminal jobs.
- Locking: per-queue `SemaphoreSlim(1,1)` with lock-timeout detection (releases stale locks).

**EF Core** (`src/Atomizer.EntityFrameworkCore/Storage/`):
- Three entity tables: `AtomizerJobEntity`, `AtomizerJobErrorEntity`, `AtomizerScheduleEntity`. Register via `modelBuilder.AddAtomizerEntities(schema: "atomizer")`.
- `GetDueJobsAsync` and `GetDueSchedulesAsync` use raw provider-specific SQL with `FOR NO KEY UPDATE SKIP LOCKED` (PostgreSQL) or equivalent for atomic batch acquisition.
- Supported providers detected via `RelationalProviderCache`: **SqlServer**, **PostgreSQL**, **MySQL**. Unsupported providers throw unless `AllowUnsafeProviderFallback = true` in `EntityFrameworkCoreJobStorageOptions`.
- Locking: `DatabaseTransactionLeasingScope` wraps a `ReadCommitted` transaction; committing the scope commits the lease writes, rollback on failure.
- Idempotency on `InsertAsync`: checks for existing job with matching `IdempotencyKey` before inserting.
- Note: `UpsertScheduleAsync` has a known race condition comment (`@todo` for optimistic concurrency).

### Configuration Entry Points

```csharp
services.AddAtomizer(options =>
{
    options.UseInMemoryStorage();                       // or UseEntityFrameworkCoreStorage<TDbContext>()
    options.AddQueue(QueueKey.Default, q => { ... });  // BatchSize=10, DegreeOfParallelism=4, VisibilityTimeout=10m, StorageCheckInterval=15s
    options.AddHandlersFrom<TMarker>();                 // scans assembly for IAtomizerJob<T> implementations
    options.ConfigureScheduling(s => { ... });
});

services.AddAtomizerProcessing(options =>
{
    options.StartupDelay = TimeSpan.FromSeconds(5);
    options.GracefulShutdownTimeout = TimeSpan.FromSeconds(30);
});
```

A default queue (`QueueKey.Default`) is always added if none is configured. Handlers are registered as `Scoped`.

### `DefaultJobDispatcher` internals

Handler resolution: `IAtomizerJobTypeResolver` maps `TPayload` → `IAtomizerJob<TPayload>` type. Handler is resolved from a new DI scope and invoked via reflection (`GetMethod("HandleAsync")`). `TargetInvocationException` is unwrapped before rethrowing so `JobProcessor` sees the real exception for retry/failure logic.

## Testing

- xUnit v3, NSubstitute for mocks, AwesomeAssertions for assertions, AutoFixture for test data.
- Test naming: `{Method}_When{Scenario}_Should{ExpectedBehavior}`.
- Test classes: `{Type}Tests`.
- Shared test helpers in `tests/Atomizer.Tests.Utilities/` — `FakeDataFactory`, `NonPublicSpy`, `TestableLogger`, sample jobs.
- EF Core integration tests use Testcontainers with per-provider database fixtures (`BaseDatabaseFixture<TDbContext>`). Each fixture starts a container, runs migrations, and exposes a `DbContext`. Supports PostgreSQL, SQL Server, MySQL, and SQLite (no container needed).

## Code Standards

- `System.Text.Json` for serialization; no Newtonsoft.
- `Microsoft.Extensions.DependencyInjection` and `Microsoft.Extensions.Logging` throughout.
- CSharpier formatting (`dotnet csharpier .`).
- XML documentation required on all public APIs.
- Main library targets `netstandard2.1` — use `#if NETCOREAPP3_0_OR_GREATER` guards for `IAsyncDisposable` and `await using` patterns.
- Models use domain methods for state transitions rather than direct property mutation. Value objects derive from `ValueObject` and implement `GetEqualityValues()`.

## Commit Style

Conventional Commits: `<type>[optional scope]: <description>` (max 72 chars).
Types: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, `revert`.
Breaking changes: append `!` after type/scope.
