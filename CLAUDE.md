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

- **`src/Atomizer`** — Core library targeting `netstandard2.0;net8.0;net10.0`. Contains all abstractions, models, processing pipeline, and in-memory storage.
- **`src/Atomizer.EntityFrameworkCore`** — EF Core storage backend targeting `net6.0;net8.0;net10.0`.

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
- `GetDueJobsAsync` and `GetDueSchedulesAsync` use raw provider-specific SQL via `ISqlDialect` with `FOR NO KEY UPDATE SKIP LOCKED` (PostgreSQL) or equivalent for atomic batch acquisition.
- Supported providers detected via `RelationalProviderCache`: **SqlServer**, **PostgreSQL**, **MySQL**. Unsupported providers throw unless `AllowUnsafeProviderFallback = true` in `EntityFrameworkCoreJobStorageOptions`.
- Locking: `DatabaseTransactionLeasingScope` wraps a `ReadCommitted` transaction; committing the scope commits the lease writes, rollback on failure.
- Idempotency on `InsertAsync`: checks for existing job with matching `IdempotencyKey` before inserting.

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
- Main library targets `netstandard2.0` — use `#if NETCOREAPP3_0_OR_GREATER` guards for `IAsyncDisposable` and `await using` patterns.
- Models use domain methods for state transitions rather than direct property mutation. Value objects derive from `ValueObject` and implement `GetEqualityValues()`.

## Commit Style

Conventional Commits: `<type>[optional scope]: <description>` (max 72 chars).
Types: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, `revert`.
Breaking changes: append `!` after type/scope.

<!-- GSD:project-start source:PROJECT.md -->
## Project

**Atomizer — Storage Refactor**

Atomizer is a background job scheduling and queueing framework for ASP.NET Core, distributed as two NuGet packages (`Atomizer` core and `Atomizer.EntityFrameworkCore`). This milestone refactors the entire storage layer to be cleaner, more correct, and extensible to non-SQL backends (MongoDB, Redis) in future milestones.

**Core Value:** A storage abstraction so clean and correct that implementing a new backend requires no tribal knowledge — just the interface.

### Constraints

- **Compatibility**: netstandard2.0 target for core library — no C# 8+ features without `#if` guards
- **Breaking change**: IAtomizerStorage and leasing abstraction changes require a major version bump
- **No Newtonsoft**: System.Text.Json only
- **Formatting**: CSharpier (`dotnet csharpier .`)
- **XML docs**: All public APIs must have XML documentation
<!-- GSD:project-end -->

<!-- GSD:stack-start source:codebase/STACK.md -->
## Technology Stack

## Runtime
- `src/Atomizer/Atomizer.csproj` multi-targets `netstandard2.0`, `net8.0`, `net10.0`.
- `src/Atomizer.EntityFrameworkCore/Atomizer.EntityFrameworkCore.csproj` multi-targets `net6.0`, `net8.0`, `net10.0`.
- All test projects multi-target `net6.0`, `net8.0`, `net10.0`.
- Samples target `net10.0` using `Microsoft.NET.Sdk.Web`.
- Lockfile restore enforced (`RestorePackagesWithLockFile=true`).
- `TreatWarningsAsErrors=true` on all `src/` projects with `NU1901–NU1904` excepted.
- `AnalysisLevel=latest`, `EnablePackageValidation=true` on both shipping projects.
- `ImplicitUsings=enable`, `Nullable=enable` across the codebase.

## Platform Requirements
- .NET SDK with support for `net10.0` (required to build `src/Atomizer` and the samples).
- Docker (for local `compose.yml` databases and Testcontainers-backed EF integration tests).
- Optional: PowerShell 7 to run `migrations.ps1` / `test-migrations.ps1`.
- A supported relational database (SqlServer, PostgreSQL, MySQL) when using EF Core storage.
<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->
## Conventions

## Naming Patterns
- One public type per file; filename matches type name (e.g. `AtomizerJob.cs`, `QueuePump.cs`).
- Tests mirror source: `{Type}Tests.cs`.
- Interfaces co-located with their default implementation when internal. Public abstractions live under `src/Atomizer/Abstractions/`.
- Classes: `PascalCase`. Interfaces: `IPascalCase`. Exceptions: `{Name}Exception`. Methods: `PascalCase`, async ends with `Async`. Private fields: `_camelCase`.
- Enums: `PascalCase` with explicit numeric values when persisted (e.g. `AtomizerJobStatus { Pending = 1, Processing = 2, ... }`).
- Static readonly singletons: `PascalCase` (e.g. `QueueKey.Default`, `RetryStrategy.Default`).

## Code Style
- Tool: CSharpier. Run `dotnet csharpier .` before committing.
- Config: `.csharpierrc` — `printWidth: 120`, `useTabs: false`, `indentSize: 4`.
- File-scoped namespaces everywhere.
- `<LangVersion>14</LangVersion>` for source, `12` for test projects.

## Access Modifiers
- Public: stable consumer-facing APIs under `src/Atomizer/Abstractions/`, value objects, `AtomizerJob`, `AtomizerSchedule`, configuration options, `JobContext`, and exceptions.
- Internal: processing pipeline, schedulers, default implementations, and EF Core storage internals. Every file in `src/Atomizer/Processing/` uses `internal sealed class`.
- `sealed` is applied to all internal implementations and value objects. Only base classes (`Model`, `ValueObject`) are non-sealed.

## Value Object Pattern
- Override `GetEqualityValues()` yielding the components that define equality.
- `Equals`, `GetHashCode`, and `==`/`!=` operators are implemented in the base class.
- Instances are immutable (`public sealed class`, no public setters).
- Concrete types: `QueueKey`, `JobKey`, `LeaseToken`, `RetryStrategy`, `Schedule`, `WorkerId` — all in `src/Atomizer/Models/ValueObjects/`.

## Error Handling
- Derive from `ArgumentException` for invalid inputs: `InvalidRetryStrategyException`, `InvalidQueueKeyException`, `InvalidJobKeyException`, `InvalidLeaseTokenException`.
- Derive from `Exception` for operational failures: `InvalidAtomizerConfigurationException`, `JobResolverException`, `PayloadSerializationException`.
- Throw domain-specific exceptions at public API boundaries and inside value-object constructors. Never throw bare `Exception`.
- Use `InvalidOperationException` for illegal state transitions on aggregates.
- Never let exceptions escape background loops. Catch → log → continue.
- Unwrap `TargetInvocationException` before rethrowing (`DefaultJobDispatcher`).

## Cancellation Token Flow
- `_ioCts` — cancelled first during shutdown to stop the poller and signal workers to stop reading.
- `_executionCts` — cancelled only when the graceful shutdown deadline expires; interrupts running handlers.
- I/O / polling / storage calls take the `ioToken`. Handler invocation takes the `executionToken` via `JobContext.CancellationToken`.
- Every async method has a `CancellationToken cancellationToken` parameter.
- Public `IAtomizerClient` methods use `CancellationToken cancellation = default`; internal / storage APIs use `CancellationToken cancellationToken` with no default.

## Logging
- **Structured logging only** — use placeholder names, never string interpolation.
- Conventional property names: `{JobId}`, `{Queue}`, `{QueueKey}`, `{Attempt}`, `{InstanceId}`, `{Ms}`, `{Delay}`.
- When logging an exception, the exception is the **first** argument: `_logger.LogError(ex, "...", args)`.

## Serialization
- Use **`System.Text.Json`** exclusively. Newtonsoft is forbidden.
- Payload serialization is centralized in `DefaultJobSerializer` — use `IAtomizerJobSerializer`, not `JsonSerializer` directly.
- When serialization fails, throw `PayloadSerializationException` (pass `deserialization: true` for read paths).

## Dependency Injection
- All registration lives in `ServiceCollectionExtensions.cs` (`AddAtomizer`, `AddAtomizerProcessing`).
- Handlers registered **Scoped** via `AtomizerOptions.AddHandlersFrom(...)`, resolved inside a fresh `IAtomizerServiceScope` per dispatch.
- Factories for pipeline components used instead of injecting components directly.

## XML Documentation
- `<summary>` is mandatory on all public APIs. `<param>` for every parameter, `<returns>` for non-void returns.
- `<remarks>` for defaults and behavioral notes on option properties.
- Internal types don't require XML docs.

## Function Design
- Methods typically stay short (<80 lines). Longer methods use numbered step comments (`// 1) ... // 2) ...`).
- Early returns and guard clauses over nested `if/else`.
- `CancellationToken` is always the last parameter.
- Prefer `IReadOnlyList<T>` / `IEnumerable<T>` at boundaries for storage reads.

## Module Design
- Public consumer API types sit in the root `namespace Atomizer;`. Use `// ReSharper disable once CheckNamespace` when the file lives in a subfolder.
- Internal namespaces mirror folder layout: `Atomizer.Core`, `Atomizer.Processing`, `Atomizer.Scheduling`, `Atomizer.Storage`, `Atomizer.Models.Base`, `Atomizer.Exceptions`, `Atomizer.Abstractions`.
- No barrel / `GlobalUsings.cs` files in source projects.
<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->
## Architecture

## Component Responsibilities
| Component | Responsibility | File |
|-----------|----------------|------|
| `IAtomizerClient` | Public enqueue / schedule API | `src/Atomizer/Abstractions/IAtomizerClient.cs` |
| `AtomizerClient` | Serializes payload, creates `AtomizerJob` / `AtomizerSchedule`, delegates to storage | `src/Atomizer/Core/AtomizerClient.cs` |
| `IAtomizerStorage` | Storage contract (insert/update jobs, lease batches, upsert schedules, release leases) | `src/Atomizer/Abstractions/IAtomizerStorage.cs` |
| `InMemoryStorage` | In-process storage using `ConcurrentDictionary`; evicts terminal jobs beyond retention | `src/Atomizer/Storage/InMemoryStorage.cs` |
| `EntityFrameworkCoreStorage<TDbContext>` | Relational storage using EF Core entities; delegates raw SQL to `ISqlDialect` | `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` |
| `IAtomizerLeasingScopeFactory` | Creates distributed-lock scopes per queue during polling | `src/Atomizer/Abstractions/IAtomizerLeasingScopeFactory.cs` |
| `InMemoryLeasingScopeFactory` | Per-queue `SemaphoreSlim` with stale-lock timeout detection | `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs` |
| `DatabaseTransactionLeasingScope` | Wraps an EF Core `ReadCommitted` transaction as a lease scope | `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScope.cs` |
| `NoopLeasingScopeFactory` | Always-acquired no-op scope; default when no lock provider is configured | `src/Atomizer/Core/NoopLeasingScopeFactory.cs` |
| `AtomizerQueueService` | `BackgroundService` that boots `QueueCoordinator` and orchestrates shutdown | `src/Atomizer/Processing/AtomizerQueueService.cs` |
| `QueueCoordinator` | Creates one `QueuePump` per configured queue, drives start/stop | `src/Atomizer/Processing/QueueCoordinator.cs` |
| `QueuePump` | Owns a bounded `Channel<AtomizerJob>`, poller task, and N workers; manages two CTS tokens for graceful shutdown | `src/Atomizer/Processing/QueuePump.cs` |
| `QueuePoller` | Polls storage on interval, leases due jobs within a leasing scope, writes to channel | `src/Atomizer/Processing/QueuePoller.cs` |
| `JobWorker` | Reads from channel, delegates each job to a `JobProcessor` | `src/Atomizer/Processing/JobWorker.cs` |
| `JobProcessor` | Calls `Attempt`, dispatches, handles success/failure, applies retry strategy, persists state | `src/Atomizer/Processing/JobProcessor.cs` |
| `IAtomizerJobDispatcher` / `DefaultJobDispatcher` | Resolves `IAtomizerJob<TPayload>` from a fresh DI scope and invokes `HandleAsync` via reflection | `src/Atomizer/Core/DefaultJobDispatcher.cs` |
| `IAtomizerJobTypeResolver` / `DefaultJobTypeResolver` | Caches `TPayload → IAtomizerJob<TPayload>` type lookups | `src/Atomizer/Core/DefaultJobTypeResolver.cs` |
| `IAtomizerJobSerializer` / `DefaultJobSerializer` | JSON serialization of payloads (`System.Text.Json` web defaults) | `src/Atomizer/Core/DefaultJobSerializer.cs` |
| `AtomizerSchedulerService` | `BackgroundService` for the scheduling loop | `src/Atomizer/Scheduling/AtomizerSchedulerService.cs` |
| `SchedulePoller` | Polls due schedules inside a leasing scope (`QueueKey.Scheduler`), advances `NextRunAt` | `src/Atomizer/Scheduling/SchedulePoller.cs` |
| `ScheduleProcessor` | Produces occurrences per `MisfirePolicy`, inserts one `AtomizerJob` per occurrence with idempotency key `{JobKey}:*:{occurrence:O}` | `src/Atomizer/Scheduling/ScheduleProcessor.cs` |
| `ISqlDialect` + `PostgreSqlDialect` / `SqlServerDialect` / `MySqlDialect` | Provider-specific raw SQL for atomic batch lease with `FOR NO KEY UPDATE SKIP LOCKED` or equivalent | `src/Atomizer.EntityFrameworkCore/Providers/Sql/*.cs` |
| `RelationalProviderCache` | Detects EF Core provider, constructs matching `ISqlDialect`, caches per-provider `EntityMap` | `src/Atomizer.EntityFrameworkCore/Providers/RelationalProviderCache.cs` |
| `AtomizerRuntimeIdentity` | Stable per-process `InstanceId` (env var `ATOMIZER_INSTANCE_ID` or `MachineName+{guid8}`) | `src/Atomizer/Core/AtomizerRuntimeIdentity.cs` |
| `IAtomizerClock` / `AtomizerClock` | Ambient clock abstraction (`UtcNow`, `MinValue`, `MaxValue`) — singleton; swap in tests | `src/Atomizer/Core/AtomizerClock.cs` |

## Architectural Constraints
- **Target frameworks:** `Atomizer` targets `netstandard2.0;net8.0;net10.0`. Use `#if NETCOREAPP3_0_OR_GREATER` for `IAsyncDisposable` / `await using`.
- **Threading model:** Per-queue pipeline uses a single poller task plus `DegreeOfParallelism` worker tasks, communicating through a `BoundedChannel<AtomizerJob>` of capacity `DegreeOfParallelism × BatchSize`. Scheduler uses a single loop task.
- **Cancellation model:** Two `CancellationTokenSource`s per pump/scheduler — `_ioCts` (shutdown → stop intake) and `_executionCts` (grace period expired → interrupt handlers). `OperationCanceledException` is always filtered by the appropriate token.
- **Channel writer:** `SingleWriter = true`, `SingleReader = false`, `FullMode = BoundedChannelFullMode.Wait`.
- **Lease token format:** `"{InstanceId}:*:{QueueKey}:*:{LeaseId}"`, parsed using literal `":*:"` delimiter.
- **Reserved queue keys:** `QueueKey.Default = "default"` and `QueueKey.Scheduler = "scheduler"`; queue names ≤100 chars, job keys ≤255 chars.
- **Static caches:** `InMemoryLeasingScopeFactory.Semaphores` and `RelationalProviderCache.Instances` are static — shared across all factory instances in the process.
- **Supported EF Core providers:** `PostgreSql`, `MySql`, `SqlServer`. Others fall back to LINQ only when `AllowUnsafeProviderFallback = true`; otherwise `GetDueJobsAsync` / `GetDueSchedulesAsync` throw `NotSupportedException`.
- **Clock:** Never call `DateTimeOffset.UtcNow` directly inside pipeline code — resolve `IAtomizerClock`.

## Anti-Patterns
- Mutating `AtomizerJob` status directly (use domain methods)
- Resolving handlers outside a DI scope
- Skipping the leasing scope when polling storage
- Throwing `TargetInvocationException` out of a handler
- Using `DateTime.UtcNow` inside pipeline code
- Adding a new EF Core provider without updating `RelationalProviderCache`
<!-- GSD:architecture-end -->

<!-- GSD:skills-start source:skills/ -->
## Project Skills

No project skills found. Add skills to any of: `.claude/skills/`, `.agents/skills/`, `.cursor/skills/`, `.github/skills/`, or `.codex/skills/` with a `SKILL.md` index file.
<!-- GSD:skills-end -->

<!-- GSD:workflow-start source:GSD defaults -->
## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:
- `/gsd-quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd-debug` for investigation and bug fixing
- `/gsd-execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->

<!-- GSD:profile-start -->
## Developer Profile

> Profile not yet configured. Run `/gsd-profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->
