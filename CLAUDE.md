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

<!-- GSD:project-start source:PROJECT.md -->
## Project

**Atomizer — Storage Refactor**

Atomizer is a background job scheduling and queueing framework for ASP.NET Core, distributed as two NuGet packages (`Atomizer` core and `Atomizer.EntityFrameworkCore`). This milestone refactors the entire storage layer to be cleaner, more correct, and extensible to non-SQL backends (MongoDB, Redis) in future milestones.

**Core Value:** A storage abstraction so clean and correct that implementing a new backend requires no tribal knowledge — just the interface.

### Constraints

- **Compatibility**: netstandard2.1 target for core library — no C# 8+ features without `#if` guards
- **Breaking change**: IAtomizerStorage and leasing abstraction changes require a major version bump
- **No Newtonsoft**: System.Text.Json only
- **Formatting**: CSharpier (`dotnet csharpier .`)
- **XML docs**: All public APIs must have XML documentation
<!-- GSD:project-end -->

<!-- GSD:stack-start source:codebase/STACK.md -->
## Technology Stack

## Languages
- C# (`LangVersion` 14 for `src/`, 12 for `tests/`) — all library, sample, and test code.
- PowerShell — operational scripts `migrations.ps1` and `test-migrations.ps1` at repo root.
- YAML — `compose.yml` for local dev databases.
## Runtime
- `src/Atomizer/Atomizer.csproj` multi-targets `netstandard2.0`, `net8.0`, `net10.0`.
- `src/Atomizer.EntityFrameworkCore/Atomizer.EntityFrameworkCore.csproj` multi-targets `net6.0`, `net8.0`, `net10.0`.
- All test projects (`tests/Atomizer.Tests`, `tests/Atomizer.EntityFrameworkCore.Tests`, `tests/Atomizer.Tests.Utilities`) multi-target `net6.0`, `net8.0`, `net10.0`.
- Samples (`samples/Atomizer.Example`, `samples/Atomizer.EFCore.Example`) target `net10.0` using `Microsoft.NET.Sdk.Web`.
- ASP.NET Core / .NET Generic Host — consumed via `Microsoft.Extensions.Hosting.Abstractions` (`IHostedService` is the integration point for `AtomizerQueueService` and `AtomizerSchedulerService`).
- NuGet, `PackageReference` style with per-project lockfiles (`RestorePackagesWithLockFile=true`).
- Lockfiles present: `src/Atomizer/packages.lock.json`, `src/Atomizer.EntityFrameworkCore/packages.lock.json`.
## Frameworks
- `Microsoft.Extensions.DependencyInjection.Abstractions` `[6.0.0, )` — DI primitives (`IServiceProvider`, `ServiceLifetime`).
- `Microsoft.Extensions.Hosting.Abstractions` `[6.0.0, )` — `IHostedService`, graceful shutdown hooks.
- `Microsoft.Extensions.Logging.Abstractions` `[6.0.0, )` — `ILogger<T>` throughout pipeline and storage.
- `System.Text.Json` `[6.0.0, )` — the only supported job payload serializer.
- `System.Threading.Channels` `[6.0.0, )` — `BoundedChannel<AtomizerJob>` inside `QueuePump` producer/consumer.
- `Cronos` `0.11.1` — cron-expression parsing for `Schedule` value object (seconds-level, 5- and 6-part cron).
- `Microsoft.EntityFrameworkCore` `6.0.0` — ORM base.
- `Microsoft.EntityFrameworkCore.Relational` `6.0.0` — raw SQL execution used by `EntityFrameworkCoreStorage`.
- `xunit.v3` `2.0.1` — test runner (see `tests/Atomizer.Tests/Atomizer.Tests.csproj`).
- `xunit.runner.visualstudio` `3.0.2`.
- `Microsoft.NET.Test.Sdk` `17.13.0`.
- `AwesomeAssertions` `9.1.0` — assertion library.
- `NSubstitute` `5.3.0` — mocking library.
- `AutoFixture` `4.18.1` — test data generation (unit tests only).
- `coverlet.collector` `6.0.4` — coverage collection.
- `Testcontainers.MsSql`, `Testcontainers.MySql`, `Testcontainers.PostgreSql` `4.6.0` — per-provider integration test fixtures in `tests/Atomizer.EntityFrameworkCore.Tests/Fixtures/`.
- `Microsoft.NET.Sdk` (library/test projects) and `Microsoft.NET.Sdk.Web` (samples).
- CSharpier — formatting (`.csharpierrc`: `printWidth: 120`, `indentSize: 4`, `useTabs: false`). Run via `dotnet csharpier .`.
- Lockfile restore enforced (`RestorePackagesWithLockFile=true`).
- `TreatWarningsAsErrors=true` on all `src/` projects with `NU1901,NU1902,NU1903,NU1904` excepted (vulnerability warnings are non-blocking).
- `AnalysisLevel=latest`, `EnablePackageValidation=true` on both shipping projects.
- `ImplicitUsings=enable`, `Nullable=enable` across the codebase.
## Key Dependencies
- `Cronos` `0.11.1` — drives `Schedule.GetOccurrences()` and `UpdateNextOccurence` in `src/Atomizer/Scheduling/`.
- `System.Threading.Channels` `6.0.0` — backbone of the per-queue pump at `src/Atomizer/Processing/QueuePump.cs`.
- `System.Text.Json` `6.0.0` — only serializer; explicit convention (no Newtonsoft).
- `Microsoft.EntityFrameworkCore` `6.0.0` — minimum supported EF Core version for the storage package.
- `Swashbuckle.AspNetCore` `9.0.3` — sample-only (Swagger UI in `samples/*/`).
- `Microsoft.EntityFrameworkCore.Design` `9.0.4` — sample-only for `dotnet ef migrations`.
## Configuration
- Author `Mikkel Buhl`.
- License `MIT` via `PackageLicenseExpression`.
- Repo `https://github.com/mnbuhl/Atomizer`.
- Package tags include `atomizer;queue;queueing;scheduler;scheduling;jobs;tasks;background`.
- `README.md` and `LICENSE` packed into NuGet output.
- `src/Atomizer/Atomizer.csproj` — `PackageId=Atomizer`, description "Atomizer provides background job queue and scheduler for .NET applications".
- `src/Atomizer.EntityFrameworkCore/Atomizer.EntityFrameworkCore.csproj` — `PackageId=Atomizer.EntityFrameworkCore`, appends `efcore;entityframeworkcore` to package tags.
- `services.AddAtomizer(options => { ... })` — core registration (queues, handlers, scheduling, storage selector).
- `services.AddAtomizerProcessing(options => { ... })` — `StartupDelay`, `GracefulShutdownTimeout`.
- `options.UseInMemoryStorage()` — default in-process backend.
- `options.UseEntityFrameworkCoreStorage<TDbContext>()` — implemented in `src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs`; registers `EntityFrameworkCoreStorage<TDbContext>` and `DatabaseTransactionLeasingScopeFactory<TDbContext>` as `Scoped`.
- `modelBuilder.AddAtomizerEntities(schema: "atomizer")` — from `src/Atomizer.EntityFrameworkCore/Extensions/ModelBuilderExtensions.cs`; required in consumer `DbContext.OnModelCreating`.
- `/Users/mbuhl/projects/Atomizer/Atomizer.sln`
- `/Users/mbuhl/projects/Atomizer/src/Directory.Build.props`
- `/Users/mbuhl/projects/Atomizer/.csharpierrc`
- `/Users/mbuhl/projects/Atomizer/compose.yml`
- `/Users/mbuhl/projects/Atomizer/migrations.ps1`
- `/Users/mbuhl/projects/Atomizer/test-migrations.ps1`
- No `appsettings.json` required by the library; consumers provide DI, connection strings, and a `DbContext`.
- `compose.yml` honors `DB_ROOT_PASSWORD` env var (defaulting to `Passw0rd!`) for local Postgres/MySQL/MSSQL containers.
## Platform Requirements
- .NET SDK with support for `net10.0` (required to build `src/Atomizer` and the samples).
- Docker (for local `compose.yml` databases and Testcontainers-backed EF integration tests).
- Optional: PowerShell 7 to run `migrations.ps1` / `test-migrations.ps1`.
- Any .NET Standard 2.0+, .NET 8, or .NET 10 host for the core library.
- Any .NET 6, .NET 8, or .NET 10 host for the EF Core storage backend.
- A supported relational database (SqlServer, PostgreSQL, MySQL) when using EF Core storage; see `INTEGRATIONS.md`.
<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->
## Conventions

## Naming Patterns
- One public type per file; filename matches type name (e.g. `AtomizerJob.cs`, `QueuePump.cs`).
- Tests mirror source: `{Type}Tests.cs` (e.g. `src/Atomizer/Models/ValueObjects/RetryStrategy.cs` → `tests/Atomizer.Tests/Models/ValueObjects/RetryStrategyTests.cs`).
- Interfaces are co-located with their default implementation when they are internal (e.g. `IJobProcessor` declared at the top of `src/Atomizer/Processing/JobProcessor.cs`). Public abstractions live under `src/Atomizer/Abstractions/`.
- Classes: `PascalCase`, e.g. `AtomizerJob`, `QueuePump`, `DefaultJobDispatcher`.
- Interfaces: `IPascalCase`, e.g. `IAtomizerStorage`, `IAtomizerJob<TPayload>`, `IJobWorker`.
- Exceptions: `{Name}Exception` (e.g. `InvalidQueueKeyException`, `JobResolverException`). See `src/Atomizer/Exceptions/`.
- Enums: `PascalCase` with explicit numeric values when persisted (e.g. `AtomizerJobStatus { Pending = 1, Processing = 2, ... }` in `src/Atomizer/Models/AtomizerJob.cs:116-121`).
- Methods: `PascalCase`. Async methods end with `Async` (e.g. `InsertAsync`, `DispatchAsync`, `HandleAsync`).
- Properties: `PascalCase`.
- Parameters and local variables: `camelCase`.
- Private fields: `_camelCase` with leading underscore (e.g. `private readonly IAtomizerClock _clock;` in `src/Atomizer/Core/AtomizerClient.cs:10`).
- Static readonly singletons: `PascalCase` (e.g. `QueueKey.Default`, `RetryStrategy.Default`).
- `ProcessAsync_WhenJobSucceeds_ShouldMarkCompletedAndUpdateStorageAndLog`
- `ProcessAsync_WhenOperationCanceled_ShouldLogWarning`
- `HandleFailureAsync_WhenShouldNotRetry_ShouldMarkFailedAndLogError`
## Code Style
- Tool: CSharpier. Run `dotnet csharpier .` before committing.
- Config: `.csharpierrc` at repo root — `printWidth: 120`, `useTabs: false`, `indentSize: 4`, `endOfLine: auto`.
- File-scoped namespaces everywhere (e.g. `namespace Atomizer.Processing;` in `src/Atomizer/Processing/JobProcessor.cs:5`).
- `<Nullable>enable</Nullable>` — nullable reference types required.
- `<ImplicitUsings>enable</ImplicitUsings>` for source projects.
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — warnings break the build (except NuGet advisory warnings `NU1901-NU1904`).
- `<AnalysisLevel>latest</AnalysisLevel>` — latest Roslyn analyzers.
- `<EnablePackageValidation>true</EnablePackageValidation>` for public-facing package stability.
- `<LangVersion>14</LangVersion>` for source, `12` for test projects.
- `src/Atomizer/Atomizer.csproj`: `netstandard2.0;net8.0;net10.0`.
- `src/Atomizer.EntityFrameworkCore/Atomizer.EntityFrameworkCore.csproj`: `net6.0;net8.0;net10.0`.
- Tests: `net6.0;net8.0;net10.0`.
## Multi-Targeting Patterns
#if NETCOREAPP3_0_OR_GREATER
#endif
#if NETCOREAPP3_0_OR_GREATER
#else
#endif
## Import Organization
- Tests use `global using` aggregated in `tests/Atomizer.Tests/globals.cs`:
- Source projects rely on `<ImplicitUsings>enable</ImplicitUsings>` for common `System.*` usings.
## Access Modifiers
- Public: stable consumer-facing APIs under `src/Atomizer/Abstractions/`, value objects (`QueueKey`, `JobKey`, `RetryStrategy`, `Schedule`), `AtomizerJob`, `AtomizerSchedule`, configuration options (`AtomizerOptions`, `QueueOptions`, `SchedulingOptions`), `JobContext`, and exceptions.
- Internal: processing pipeline, schedulers, default implementations, and EF Core storage internals. Every file in `src/Atomizer/Processing/` uses `internal sealed class` (e.g. `QueuePump`, `JobWorker`, `JobProcessor`).
- `sealed` is applied to all internal implementations and to value objects (`public sealed class QueueKey` at `src/Atomizer/Models/ValueObjects/QueueKey.cs:6`). Only base classes (`Model`, `ValueObject`) are non-sealed.
## Domain Method Pattern
## Value Object Pattern
- Override `GetEqualityValues()` yielding the components that define equality.
- `Equals`, `GetHashCode`, and `==`/`!=` operators are implemented in the base class.
- Instances are immutable (`public sealed class`, no public setters, e.g. `QueueKey.Key` is init-only via constructor).
## Error Handling
- Derive from `ArgumentException` for invalid inputs with a `paramName` overload: `InvalidRetryStrategyException`, `InvalidQueueKeyException`, `InvalidJobKeyException`, `InvalidLeaseTokenException`.
- Derive from `Exception` for operational / configuration failures: `InvalidAtomizerConfigurationException`, `JobResolverException`, `PayloadSerializationException`.
- Each exception exposes four-ish constructors: `(message)`, `(message, innerException)`, `(message, paramName)` for `ArgumentException`-derived, and `(message, paramName, innerException)`.
- Throw domain-specific exceptions at public API boundaries and inside value-object constructors. Never throw bare `Exception`.
- Use `InvalidOperationException` for illegal state transitions on aggregates (see `AtomizerJob.Lease`).
- In the processing pipeline, catch `OperationCanceledException` separately so shutdown is not treated as a failure:
- Never let exceptions escape background loops (poller, worker, pump). Catch → log → continue. The only exception is `HandleFailureAsync` itself, which also catches and logs so storage failures never crash the host.
- Unwrap `TargetInvocationException` before rethrowing so retry/failure logic sees the real exception (`DefaultJobDispatcher` in `src/Atomizer/Core/DefaultJobDispatcher.cs`).
## Cancellation Token Flow
- `_ioCts` — linked to the host cancellation token. Cancelled first during shutdown to stop the poller and signal workers to stop reading from the channel.
- `_executionCts` — cancelled only when the graceful shutdown deadline expires; interrupts running handlers mid-flight.
- I/O / polling / storage calls take the `ioToken`.
- User handler invocation (`IAtomizerJob<T>.HandleAsync`) takes the `executionToken` via `JobContext.CancellationToken` (`src/Atomizer/Abstractions/IAtomizerJob.cs:18`).
- Every async method has a `CancellationToken cancellationToken` parameter.
- Public `IAtomizerClient` methods use `CancellationToken cancellation = default` (see `src/Atomizer/Abstractions/IAtomizerClient.cs:9`); internal / storage APIs use `CancellationToken cancellationToken` with no default.
- Do not pass `CancellationToken.None` unless the call site is the shutdown cleanup path (e.g. `QueuePump.StopAsync` worker task, `src/Atomizer/Processing/QueuePump.cs:80-83`).
## Logging
- **Structured logging only** — use placeholder names, never string interpolation:
- Conventional property names: `{JobId}`, `{Queue}`, `{QueueKey}`, `{Attempt}`, `{InstanceId}`, `{Ms}`, `{Delay}`.
- Log levels:
- When logging an exception, the exception is the **first** argument, followed by the template: `_logger.LogError(ex, "Error releasing leased jobs for queue '{QueueKey}' ...", _queue.QueueKey);`.
## Serialization
- Use **`System.Text.Json`** exclusively. Newtonsoft is forbidden.
- Payload serialization is centralized in `src/Atomizer/Core/DefaultJobSerializer.cs` — downstream code uses `IAtomizerJobSerializer` rather than calling `JsonSerializer` directly.
- When serialization fails, throw `PayloadSerializationException` (pass `deserialization: true` for read paths) rather than letting raw `JsonException` escape.
## Dependency Injection
- **`Microsoft.Extensions.DependencyInjection`** throughout. No other DI containers.
- All registration lives in `src/Atomizer/Configuration/ServiceCollectionExtensions.cs` (`AddAtomizer`, `AddAtomizerProcessing`).
- Storage backends are registered via `JobStorageOptions` extension points (`UseInMemoryStorage`, `UseEntityFrameworkCoreStorage<TDbContext>`) — see `src/Atomizer/Configuration/JobStorageOptions.cs` and `src/Atomizer.EntityFrameworkCore/Extensions/`.
- Handlers (`IAtomizerJob<T>` implementations) are registered **Scoped** via `AtomizerOptions.AddHandlersFrom(...)` (`src/Atomizer/Configuration/AtomizerOptions.cs:44`) and resolved inside a fresh `IAtomizerServiceScope` per dispatch.
- Factories for pipeline components (`IJobWorkerFactory`, `IJobProcessorFactory`, `IQueuePumpFactory`) are used instead of injecting the components directly, so each queue gets a dedicated instance.
## XML Documentation
- `<summary>` is mandatory.
- `<param>` for every parameter, `<returns>` for non-void returns. See `src/Atomizer/Abstractions/IAtomizerStorage.cs:5-12` for the canonical example.
- `<remarks>` is used to state defaults and behavioral notes on option properties (see `src/Atomizer/Abstractions/IAtomizerClient.cs:42`).
- `<see cref="..."/>` is used in summaries to cross-reference related types (see test-class summaries such as `JobProcessorTests`).
- Internal types don't require XML docs, but public interfaces and their option bags must have them.
## Function Design
- Methods typically stay short (<80 lines). Longer methods (`QueuePump.StopAsync`, `src/Atomizer/Processing/QueuePump.cs:89-151`) are annotated with numbered step comments (`// 1) ... // 2) ...`).
- Early returns and guard clauses over nested `if/else`.
- `return new Foo { ... }` object-initializer style preferred over multi-statement construction (see `AtomizerJob.Create`).
- Collection initializers with modern syntax: `[]`, `new List<T>()`, `new[] { ... }`. The codebase mixes `[]` and `Array.Empty<T>()`; prefer `[]` in new code (C# 12+).
- Use `Action<TOptions>? configure = null` for optional fluent configuration (see `EnqueueAsync` in `src/Atomizer/Abstractions/IAtomizerClient.cs:6-10` and `AtomizerOptions.AddQueue`).
- `CancellationToken` is always the last parameter.
- Prefer `IReadOnlyList<T>` / `IEnumerable<T>` at boundaries for storage reads (`IAtomizerStorage.GetDueJobsAsync`).
## Module Design
- Public consumer API types sit in the root `namespace Atomizer;` (e.g. `QueueKey`, `RetryStrategy`, `AtomizerJob`, `IAtomizerClient`, `JobContext`, `AtomizerOptions`). Several abstraction files explicitly annotate `// ReSharper disable once CheckNamespace` to keep them in the root namespace even though they live in a subfolder (`src/Atomizer/Abstractions/IAtomizerJob.cs:1`).
- Internal namespaces mirror folder layout: `Atomizer.Core`, `Atomizer.Processing`, `Atomizer.Scheduling`, `Atomizer.Storage`, `Atomizer.Models.Base`, `Atomizer.Exceptions`, `Atomizer.Abstractions` (for `IAtomizerStorage` / internal abstractions).
- EF Core project: `Atomizer.EntityFrameworkCore`, `Atomizer.EntityFrameworkCore.Storage`, `Atomizer.EntityFrameworkCore.Providers`, etc.
- No barrel / `GlobalUsings.cs` files in source projects.
- New consumer-facing types should be placed in the root `Atomizer` namespace, using the `// ReSharper disable once CheckNamespace` comment when the file lives in a subfolder.
<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->
## Architecture

## System Overview
```text
```
## Component Responsibilities
| Component | Responsibility | File |
|-----------|----------------|------|
| `IAtomizerClient` | Public enqueue / schedule API | `src/Atomizer/Abstractions/IAtomizerClient.cs` |
| `AtomizerClient` | Implementation: serializes payload, creates `AtomizerJob` / `AtomizerSchedule`, delegates to storage | `src/Atomizer/Core/AtomizerClient.cs` |
| `IAtomizerStorage` | Storage contract (insert/update jobs, lease batches, upsert schedules, release leases) | `src/Atomizer/Abstractions/IAtomizerStorage.cs` |
| `InMemoryStorage` | In-process storage using `ConcurrentDictionary`; evicts terminal jobs beyond retention | `src/Atomizer/Storage/InMemoryStorage.cs` |
| `EntityFrameworkCoreStorage<TDbContext>` | Relational storage using EF Core entities; uses provider-specific raw SQL for due-job fetches | `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` |
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
| `Scheduler` | Starts/stops the single-task schedule loop with two CTS tokens | `src/Atomizer/Scheduling/Scheduler.cs` |
| `SchedulePoller` | Polls due schedules inside a leasing scope (`QueueKey.Scheduler`), advances `NextRunAt` | `src/Atomizer/Scheduling/SchedulePoller.cs` |
| `ScheduleProcessor` | Produces occurrences per `MisfirePolicy`, inserts one `AtomizerJob` per occurrence with idempotency key `{JobKey}:*:{occurrence:O}` | `src/Atomizer/Scheduling/ScheduleProcessor.cs` |
| `RelationalProviderCache` | Detects EF Core provider and exposes the matching `IDatabaseProviderSql`; caches per-provider `EntityMap` | `src/Atomizer.EntityFrameworkCore/Providers/RelationalProviderCache.cs` |
| `IDatabaseProviderSql` + `PostgreSqlProvider` / `SqlServerProvider` / `MySqlProvider` | Provider-specific raw SQL for atomic batch lease with `FOR NO KEY UPDATE SKIP LOCKED` or equivalent | `src/Atomizer.EntityFrameworkCore/Providers/Sql/*.cs` |
| `AtomizerRuntimeIdentity` | Stable per-process `InstanceId` (env var `ATOMIZER_INSTANCE_ID` or `MachineName+{guid8}`) | `src/Atomizer/Core/AtomizerRuntimeIdentity.cs` |
| `IAtomizerClock` / `AtomizerClock` | Ambient clock abstraction (`UtcNow`, `MinValue`, `MaxValue`) — singleton; swap in tests | `src/Atomizer/Core/AtomizerClock.cs` |
## Pattern Overview
- Layered separation between public API (`IAtomizerClient`), domain (`AtomizerJob`, `AtomizerSchedule`, value objects), infrastructure (`IAtomizerStorage`, `IAtomizerLeasingScopeFactory`), and execution (`Processing/`, `Scheduling/`).
- Pluggable storage via `JobStorageOptions` (factory + `ServiceLifetime`) and pluggable locking via `LeasingScopeOptions`.
- Producer/consumer within a queue uses `System.Threading.Channels.Channel<AtomizerJob>` bounded to `DegreeOfParallelism × BatchSize`.
- Every job is handled in a fresh `IServiceScope`, so handlers registered as `Scoped` see per-job lifetimes (see `DefaultJobDispatcher`).
- Two cancellation tokens (I/O vs. execution) are threaded through the pipeline for two-phase graceful shutdown.
- Distributed coordination is an abstraction (`IAtomizerLeasingScopeFactory`); the EF Core backend layers it on top of `FOR NO KEY UPDATE SKIP LOCKED` raw SQL.
## Layers
- Purpose: Surface consumer-facing types (`IAtomizerClient`, `EnqueueOptions`, `RecurringOptions`, `AtomizerJob`, `AtomizerSchedule`, value objects, `JobContext`, enums).
- Location: `src/Atomizer/` (types live at `namespace Atomizer` via `// ReSharper disable once CheckNamespace` even when in subfolders).
- Contains: Abstractions, domain types, options, exceptions.
- Depends on: `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`, `Cronos`, `System.Text.Json`, `System.Threading.Channels` (all `[6.0.0, )`).
- Used by: Consumer code and the `Atomizer.EntityFrameworkCore` package.
- Purpose: Interfaces that enable pluggable storage, locking, serialization, and type resolution.
- Location: `src/Atomizer/Abstractions/`.
- Key files: `IAtomizerClient.cs`, `IAtomizerStorage.cs`, `IAtomizerJob.cs`, `IAtomizerJobSerializer.cs`, `IAtomizerServiceScope.cs`, `IAtomizerLeasingScope.cs`, `IAtomizerLeasingScopeFactory.cs`.
- Purpose: Default implementations of the abstractions, plus glue with `IServiceProvider`.
- Location: `src/Atomizer/Core/`.
- Contains: `AtomizerClient`, `AtomizerClock`, `AtomizerRuntimeIdentity`, `DefaultJobDispatcher` (+ `IAtomizerJobDispatcher`), `DefaultJobSerializer`, `DefaultJobTypeResolver` (+ `IAtomizerJobTypeResolver`), `NoopLeasingScopeFactory`, `ServiceProviderServiceScope(Factory)`.
- Depends on: Abstractions, Exceptions, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`.
- Purpose: Aggregates and value objects with enforced state transitions.
- Location: `src/Atomizer/Models/`.
- Files: `AtomizerJob.cs`, `AtomizerSchedule.cs`, `AtomizerJobError.cs`, `Base/Model.cs`, `Base/ValueObject.cs`, `ValueObjects/*.cs`.
- Rule: State changes go through domain methods (`Lease`, `Attempt`, `MarkAsCompleted`, `MarkAsFailed`, `Reschedule`, `Release`, `Disable`, `UpdateNextOccurence`).
- Purpose: Options classes + `IServiceCollection` extensions.
- Location: `src/Atomizer/Configuration/`.
- Files: `AtomizerOptions.cs`, `AtomizerOptionsExtensions.cs` (in-memory storage plug-in), `AtomizerProcessingOptions.cs`, `JobStorageOptions.cs`, `LeasingScopeOptions.cs`, `QueueOptions.cs`, `SchedulingOptions.cs`, `ServiceCollectionExtensions.cs`.
- Entry points: `services.AddAtomizer(...)` and `services.AddAtomizerProcessing(...)`.
- Purpose: Poll storage, lease batches, fan out to workers, dispatch handlers.
- Location: `src/Atomizer/Processing/`.
- Depends on: Abstractions, Core (`IAtomizerClock`, `IAtomizerJobDispatcher`, `AtomizerRuntimeIdentity`), Configuration (`AtomizerOptions`, `QueueOptions`), `System.Threading.Channels`, `Microsoft.Extensions.Hosting.Abstractions`.
- Purpose: Produce jobs from recurring schedules.
- Location: `src/Atomizer/Scheduling/`.
- Depends on: Abstractions, Core, Configuration (`SchedulingOptions`), `Cronos`.
- Location: `src/Atomizer/Storage/`.
- Files: `InMemoryStorage.cs`, `InMemoryLeasingScopeFactory.cs`, `InMemoryJobStorageOptions.cs`.
- Purpose: Relational storage and locking backend.
- Location: `src/Atomizer.EntityFrameworkCore/`.
- Structure:
- Depends on: `Atomizer` (core), `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.EntityFrameworkCore`.
## Data Flow
### Enqueue Path (`IAtomizerClient.EnqueueAsync` / `ScheduleAsync`)
### Recurring Schedule Path (`IAtomizerClient.ScheduleRecurringAsync`)
### Queue Processing Path (inbound run-loop)
### Scheduling Path (recurring fan-out)
### Shutdown Path (`AtomizerQueueService.StopAsync`)
- Jobs/schedules: persisted in `IAtomizerStorage`; in-memory state lives in `ConcurrentDictionary`s inside `InMemoryStorage`.
- Runtime: `AtomizerOptions` is registered as a singleton; `AtomizerRuntimeIdentity` is a singleton per process.
- Channels are per `QueuePump` (one per queue) and do not cross queue boundaries.
## Key Abstractions
- Purpose: Insert/update jobs and schedules, atomically lease due jobs, release leases by token, fetch due schedules.
- Examples: `src/Atomizer/Storage/InMemoryStorage.cs`, `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs`.
- Pattern: Strategy — registered via `JobStorageOptions(Func<IServiceProvider, IAtomizerStorage>, ServiceLifetime)` and wired in `ServiceCollectionExtensions.AddAtomizer` (`src/Atomizer/Configuration/ServiceCollectionExtensions.cs:42`).
- Purpose: Per-queue lock abstraction used exactly around `GetDueJobsAsync → UpdateJobsAsync` and around schedule polling.
- Examples: `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs`, `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs`, `src/Atomizer/Core/NoopLeasingScopeFactory.cs`.
- Pattern: Strategy + scope/disposable. If `Acquired == false`, the poller skips this tick.
- Purpose: Marker/interface users implement per payload type. Registered from an assembly via `AtomizerOptions.AddHandlersFrom<TMarker>()` (lifetime: `Scoped`) — see `src/Atomizer/Configuration/AtomizerOptions.cs:44`.
- Purpose: Decouples `JobProcessor` from reflection and DI wiring. Implemented by `DefaultJobDispatcher` which creates a fresh `IServiceScope` per job.
- Purpose: State transitions are enforced by domain methods; status is an integer enum persisted as-is.
- Pattern: Rich domain model (methods throw `InvalidOperationException` when state is wrong — see `AtomizerJob.Lease`, `Attempt`, `Release`).
- Equality via `GetEqualityValues()` (sequence compare).
- Concrete types: `QueueKey`, `JobKey`, `LeaseToken`, `RetryStrategy`, `Schedule`, `WorkerId` — all in `src/Atomizer/Models/ValueObjects/`.
- Most support implicit `string ⇄ T` conversions; length/format guards throw dedicated exceptions from `src/Atomizer/Exceptions/`.
- A thin wrapper around `IServiceScope` exposing `Storage` and `LeasingScopeFactory` so internal services can resolve the storage+lock pair without reaching into `IServiceProvider`.
## Entry Points
- Triggers: Application startup (`Program.cs` / DI bootstrap).
- Responsibilities:
- Triggers: Application startup (opt-in on hosts that actually execute jobs).
- Responsibilities:
- Installs `InMemoryStorage` and `InMemoryLeasingScopeFactory`.
- Installs `EntityFrameworkCoreStorage<TDbContext>` and `DatabaseTransactionLeasingScopeFactory<TDbContext>` (both `Scoped`).
- Applies the three EF `IEntityTypeConfiguration<T>`s inside a consumer `DbContext.OnModelCreating`.
## Architectural Constraints
- **Target frameworks:** `Atomizer` targets `netstandard2.0;net8.0;net10.0` with `<LangVersion>14</LangVersion>`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` (`src/Atomizer/Atomizer.csproj`). Use `#if NETCOREAPP3_0_OR_GREATER` for `IAsyncDisposable` / `await using` — see `IAtomizerLeasingScope` (`src/Atomizer/Abstractions/IAtomizerLeasingScope.cs:3`) and `QueuePoller` (`src/Atomizer/Processing/QueuePoller.cs:59`).
- **Threading model:** Per-queue pipeline uses a single poller task plus `DegreeOfParallelism` worker tasks, communicating through a `BoundedChannel<AtomizerJob>` of capacity `DegreeOfParallelism × BatchSize`. Scheduler uses a single loop task.
- **Cancellation model:** Two `CancellationTokenSource`s per pump/scheduler — `_ioCts` (cancelled on shutdown → poller/workers stop intake) and `_executionCts` (cancelled only after grace period elapses → interrupts running handlers). `OperationCanceledException` is always filtered by the appropriate token (`when (ioToken.IsCancellationRequested)`).
- **Channel writer:** `SingleWriter = true`, `SingleReader = false`, `FullMode = BoundedChannelFullMode.Wait` — only the poller writes, but multiple workers read (`src/Atomizer/Processing/QueuePump.cs:48`).
- **Lease token format:** `"{InstanceId}:*:{QueueKey}:*:{LeaseId}"`, parsed by `LeaseToken` using literal `":*:"` delimiter (`src/Atomizer/Models/ValueObjects/LeaseToken.cs:21`).
- **Reserved queue keys:** `QueueKey.Default = "default"` and internal `QueueKey.Scheduler = "scheduler"`; queue names limited to 100 chars and job keys to 255 chars.
- **Static caches:** `InMemoryLeasingScopeFactory.Semaphores` is a static `ConcurrentDictionary<QueueKey, (SemaphoreSlim, DateTimeOffset)>` — shared across all factory instances in the process (`src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs:10`). `RelationalProviderCache.Instances` is a static provider cache (`src/Atomizer.EntityFrameworkCore/Providers/RelationalProviderCache.cs:29`). `DefaultJobTypeResolver._cache` is per-instance but the resolver is a singleton.
- **Supported EF Core providers:** `PostgreSql`, `MySql`, `SqlServer` (raw SQL providers). `Sqlite`, `Oracle`, `Unknown` are unsupported and fall back to LINQ only when `EntityFrameworkCoreJobStorageOptions.AllowUnsafeProviderFallback = true`; otherwise `GetDueJobsAsync` / `GetDueSchedulesAsync` throw `NotSupportedException` (`src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:112`).
- **Serialization:** `System.Text.Json` with `JsonSerializerDefaults.Web`; Newtonsoft.Json is not used.
- **Clock:** Never call `DateTimeOffset.UtcNow` directly inside pipeline code — resolve `IAtomizerClock`. It is a singleton and freely injectable everywhere.
## Anti-Patterns
### Mutating `AtomizerJob` status directly
### Resolving handlers outside a DI scope
### Skipping the leasing scope when polling storage
### Throwing `TargetInvocationException` out of a handler
### Using `DateTime.UtcNow` inside pipeline code
### Adding a new EF Core provider without updating `RelationalProviderCache`
## Error Handling
- Typed exceptions for configuration/domain invariants: `InvalidAtomizerConfigurationException`, `InvalidJobKeyException`, `InvalidLeaseTokenException`, `InvalidQueueKeyException`, `InvalidRetryStrategyException`, `JobResolverException`, `PayloadSerializationException` — all in `src/Atomizer/Exceptions/`.
- Retry record: `AtomizerJobError.Create(jobId, timestamp, attempt, exception, instanceId)` is appended to `job.Errors` on each failed attempt (`src/Atomizer/Processing/JobProcessor.cs:79`).
- `RetryStrategy` factory validates inputs and throws `InvalidRetryStrategyException` on invalid delays / `MaxAttempts < 1` / `Exponent <= 1.0`.
- EF Core `UpsertScheduleAsync` currently catches `DbUpdateException` and logs (`src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:178`); a `@todo` notes moving to optimistic concurrency.
## Cross-Cutting Concerns
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
