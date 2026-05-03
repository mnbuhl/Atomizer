# Codebase Structure

**Analysis Date:** 2026-05-03

## Directory Layout

```
Atomizer/
├── Atomizer.sln                                # Solution file (src + tests + samples)
├── CLAUDE.md                                   # Project guidance for Claude Code
├── README.md
├── LICENSE
├── compose.yml                                 # Docker Compose for integration tests
├── migrations.ps1                              # Helper script for EF Core migrations
├── test-migrations.ps1                         # Helper script for testing migrations
├── .github/
│   └── workflows/                              # CI pipelines
├── .planning/
│   └── codebase/                               # Output of /gsd-map-codebase
├── src/
│   ├── Atomizer/                               # Core library (netstandard2.0;net8.0;net10.0)
│   │   ├── Atomizer.csproj
│   │   ├── AssemblyAttributes.cs               # InternalsVisibleTo etc.
│   │   ├── Abstractions/                       # Public & internal contracts
│   │   ├── Configuration/                      # Options + DI extensions
│   │   ├── Core/                               # Default infrastructure impls
│   │   ├── Exceptions/                         # Typed exceptions
│   │   ├── Models/                             # Aggregates, base types, value objects
│   │   │   ├── Base/                           # Model + ValueObject base types
│   │   │   └── ValueObjects/
│   │   ├── Processing/                         # Queue pipeline (hosted service)
│   │   ├── Scheduling/                         # Scheduler pipeline (hosted service)
│   │   └── Storage/                            # In-memory storage + lock factory
│   └── Atomizer.EntityFrameworkCore/           # EF Core storage backend (net6.0;net8.0;net10.0)
│       ├── Atomizer.EntityFrameworkCore.csproj
│       ├── AssemblyAttributes.cs
│       ├── Configurations/                     # IEntityTypeConfiguration<T> impls
│       ├── Entities/                           # EF entity classes + mappers
│       ├── Extensions/                         # UseEntityFrameworkCoreStorage + AddAtomizerEntities
│       ├── Providers/                          # Provider detection + raw SQL
│       │   └── Sql/                            # PostgreSql / SqlServer / MySql providers
│       └── Storage/                            # EfCore storage + transaction leasing scope
├── tests/
│   ├── Atomizer.Tests/                         # Unit tests for core library
│   │   ├── Core/
│   │   ├── Models/
│   │   │   └── ValueObjects/
│   │   ├── Processing/
│   │   ├── Scheduling/
│   │   └── Storage/
│   ├── Atomizer.EntityFrameworkCore.Tests/     # Integration tests with Testcontainers
│   │   ├── Fixtures/                           # BaseDatabaseFixture + per-provider fixtures
│   │   ├── Storage/
│   │   └── TestSetup/
│   │       ├── Postgres/
│   │       ├── SqlServer/
│   │       ├── MySql/
│   │       └── Sqlite/
│   └── Atomizer.Tests.Utilities/               # Shared test helpers
│       ├── Stubs/
│       └── TestJobs/
└── samples/
    ├── Atomizer.Example/                       # Minimal in-memory sample
    │   └── Handlers/
    └── Atomizer.EFCore.Example/                # EF Core sample
        ├── Data/
        │   ├── Postgres/
        │   ├── MySql/
        │   └── Sqlite/
        ├── Entities/
        └── Handlers/
```

## Directory Purposes

**`src/Atomizer/Abstractions/`:**
- Purpose: Interfaces consumers and the EF Core package depend on.
- Contains: `IAtomizerClient.cs` (+ `EnqueueOptions`, `RecurringOptions`), `IAtomizerJob.cs` (+ `JobContext`), `IAtomizerJobSerializer.cs`, `IAtomizerLeasingScope.cs`, `IAtomizerLeasingScopeFactory.cs`, `IAtomizerServiceScope.cs`, `IAtomizerStorage.cs`.
- Key files: `src/Atomizer/Abstractions/IAtomizerClient.cs`, `src/Atomizer/Abstractions/IAtomizerStorage.cs`.

**`src/Atomizer/Configuration/`:**
- Purpose: Options types and `IServiceCollection` extensions that bootstrap Atomizer into the host.
- Contains: `AtomizerOptions.cs`, `AtomizerOptionsExtensions.cs` (in-memory plug-in), `AtomizerProcessingOptions.cs`, `JobStorageOptions.cs`, `LeasingScopeOptions.cs`, `QueueOptions.cs`, `SchedulingOptions.cs`, `ServiceCollectionExtensions.cs`.
- Key files: `src/Atomizer/Configuration/ServiceCollectionExtensions.cs`, `src/Atomizer/Configuration/AtomizerOptions.cs`.

**`src/Atomizer/Core/`:**
- Purpose: Default implementations of abstractions; the glue layer between DI and the domain.
- Contains: `AtomizerClient.cs`, `AtomizerClock.cs`, `AtomizerRuntimeIdentity.cs`, `DefaultJobDispatcher.cs` (+ `IAtomizerJobDispatcher`), `DefaultJobSerializer.cs`, `DefaultJobTypeResolver.cs` (+ `IAtomizerJobTypeResolver`), `NoopLeasingScopeFactory.cs`, `ServiceProviderServiceScope.cs` (+ `ServiceProviderServiceScopeFactory`).
- Key files: `src/Atomizer/Core/AtomizerClient.cs`, `src/Atomizer/Core/DefaultJobDispatcher.cs`.

**`src/Atomizer/Exceptions/`:**
- Purpose: Typed exceptions for configuration, domain invariants, serialization, and resolution failures.
- Contains: `InvalidAtomizerConfigurationException.cs`, `InvalidJobKeyException.cs`, `InvalidLeaseTokenException.cs`, `InvalidQueueKeyException.cs`, `InvalidRetryStrategyException.cs`, `JobResolverException.cs`, `PayloadSerializationException.cs`.

**`src/Atomizer/Models/`:**
- Purpose: Domain aggregates, base types, and value objects.
- Contains: `AtomizerJob.cs` (+ `AtomizerJobStatus`), `AtomizerJobError.cs`, `AtomizerSchedule.cs` (+ `MisfirePolicy`), plus `Base/` and `ValueObjects/`.
- Key files: `src/Atomizer/Models/AtomizerJob.cs`, `src/Atomizer/Models/AtomizerSchedule.cs`.

**`src/Atomizer/Models/Base/`:**
- Purpose: Base types shared by domain entities.
- Contains: `Model.cs` (aggregate root with `Guid Id`), `ValueObject.cs` (equality by `GetEqualityValues()` sequence compare).

**`src/Atomizer/Models/ValueObjects/`:**
- Purpose: Value objects used throughout the domain and API surface.
- Contains: `JobKey.cs`, `LeaseToken.cs`, `QueueKey.cs` (with reserved `Default` and internal `Scheduler`), `RetryStrategy.cs` (factories `Default`, `None`, `Fixed`, `Intervals`, `Exponential`), `Schedule.cs` (6-part cron + preset constants + `Cron(string)`), `WorkerId.cs` (internal).

**`src/Atomizer/Processing/`:**
- Purpose: Hosted background service that drains queues and executes handlers.
- Contains: `AtomizerQueueService.cs`, `QueueCoordinator.cs`, `QueuePump.cs`, `QueuePumpFactory.cs`, `QueuePoller.cs`, `JobWorker.cs`, `JobWorkerFactory.cs`, `JobProcessor.cs`, `JobProcessorFactory.cs`.
- Key files: `src/Atomizer/Processing/QueuePump.cs`, `src/Atomizer/Processing/JobProcessor.cs`.

**`src/Atomizer/Scheduling/`:**
- Purpose: Hosted background service that fans recurring schedules out to jobs.
- Contains: `AtomizerSchedulerService.cs`, `Scheduler.cs`, `SchedulePoller.cs`, `ScheduleProcessor.cs`.

**`src/Atomizer/Storage/`:**
- Purpose: Built-in in-process storage backend.
- Contains: `InMemoryStorage.cs`, `InMemoryLeasingScopeFactory.cs`, `InMemoryJobStorageOptions.cs`.

**`src/Atomizer.EntityFrameworkCore/Entities/`:**
- Purpose: Plain EF entity classes with mapper extension methods (`ToEntity()` / `ToAtomizerJob()` / `ToAtomizerSchedule()`).
- Contains: `AtomizerJobEntity.cs`, `AtomizerJobErrorEntity.cs`, `AtomizerScheduleEntity.cs`.

**`src/Atomizer.EntityFrameworkCore/Configurations/`:**
- Purpose: `IEntityTypeConfiguration<T>` implementations applied by `ModelBuilder.AddAtomizerEntities`.
- Contains: `AtomizerJobEntityConfiguration.cs`, `AtomizerJobErrorEntityConfiguration.cs`, `AtomizerScheduleEntityConfiguration.cs`.

**`src/Atomizer.EntityFrameworkCore/Providers/`:**
- Purpose: Provider detection, entity-to-SQL mapping, and provider-specific raw SQL.
- Contains: `DatabaseProvider.cs` (enum), `EntityMap.cs`, `IDatabaseProviderSql.cs`, `RelationalProviderCache.cs`.

**`src/Atomizer.EntityFrameworkCore/Providers/Sql/`:**
- Purpose: One class per supported relational provider returning `FormattableString` raw SQL (with `FOR NO KEY UPDATE SKIP LOCKED` or equivalent).
- Contains: `PostgreSqlProvider.cs`, `SqlServerProvider.cs`, `MySqlProvider.cs`.

**`src/Atomizer.EntityFrameworkCore/Storage/`:**
- Purpose: EF Core storage implementation and DB-transaction leasing scope.
- Contains: `EntityFrameworkCoreStorage.cs`, `DatabaseTransactionLeasingScope.cs`, `DatabaseTransactionLeasingScopeFactory.cs`, `EntityFrameworkCoreJobStorageOptions.cs`.

**`src/Atomizer.EntityFrameworkCore/Extensions/`:**
- Purpose: Public extension methods wiring the EF Core backend into consumers.
- Contains: `AtomizerOptionsExtensions.cs` (`UseEntityFrameworkCoreStorage<TDbContext>`), `ModelBuilderExtensions.cs` (`AddAtomizerEntities`).

**`tests/Atomizer.Tests/`:**
- Purpose: xUnit v3 unit tests for the core library.
- Structure mirrors `src/Atomizer/`: `Core/`, `Models/`, `Models/ValueObjects/`, `Processing/`, `Scheduling/`, `Storage/`.

**`tests/Atomizer.EntityFrameworkCore.Tests/`:**
- Purpose: EF Core integration tests running against real databases via Testcontainers.
- Contains: `Fixtures/BaseDatabaseFixture<TDbContext>`, `Storage/` (tests), `TestSetup/{Postgres,SqlServer,MySql,Sqlite}/` (per-provider DbContexts + migrations).

**`tests/Atomizer.Tests.Utilities/`:**
- Purpose: Shared test helpers used by both test projects.
- Contains: `FakeDataFactory`, `NonPublicSpy`, `TestableLogger`, plus `TestJobs/` (sample `IAtomizerJob<T>` implementations) and `Stubs/`.

**`samples/Atomizer.Example/`:**
- Purpose: Minimal ASP.NET Core sample using `UseInMemoryStorage`.
- Contains: `Program.cs`, `Handlers/` (sample `IAtomizerJob<T>` implementations).

**`samples/Atomizer.EFCore.Example/`:**
- Purpose: ASP.NET Core sample wired to EF Core providers.
- Contains: `Program.cs`, `Entities/`, `Handlers/`, `Data/{Postgres,MySql,Sqlite}/` (per-provider `DbContext`s and migrations).

## Key File Locations

**Entry Points (consumer-facing):**
- `src/Atomizer/Configuration/ServiceCollectionExtensions.cs`: `AddAtomizer`, `AddAtomizerProcessing`.
- `src/Atomizer/Configuration/AtomizerOptionsExtensions.cs`: `UseInMemoryStorage`.
- `src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs`: `UseEntityFrameworkCoreStorage<TDbContext>`.
- `src/Atomizer.EntityFrameworkCore/Extensions/ModelBuilderExtensions.cs`: `AddAtomizerEntities`.
- `src/Atomizer/Abstractions/IAtomizerClient.cs`: Runtime API (`EnqueueAsync`, `ScheduleAsync`, `ScheduleRecurringAsync`).

**Entry Points (hosted services):**
- `src/Atomizer/Processing/AtomizerQueueService.cs`: Queue `BackgroundService`.
- `src/Atomizer/Scheduling/AtomizerSchedulerService.cs`: Scheduler `BackgroundService`.

**Configuration:**
- `src/Atomizer/Atomizer.csproj`: `TargetFrameworks=netstandard2.0;net8.0;net10.0`, `LangVersion=14`, `Nullable=enable`, `TreatWarningsAsErrors=true`, lockfile restore enabled.
- `src/Atomizer.EntityFrameworkCore/Atomizer.EntityFrameworkCore.csproj`: EF Core package (targets per `CLAUDE.md`: `net6.0;net8.0`).
- `Atomizer.sln`: Solution root.
- `compose.yml`: Docker Compose for local databases used by integration tests.

**Core Logic:**
- `src/Atomizer/Core/AtomizerClient.cs`: Client implementation (enqueue/schedule).
- `src/Atomizer/Core/DefaultJobDispatcher.cs`: Reflection-based handler dispatch.
- `src/Atomizer/Processing/QueuePump.cs`: Per-queue pipeline plumbing (channel + poller + workers).
- `src/Atomizer/Processing/JobProcessor.cs`: Attempt / success / retry / fail logic.
- `src/Atomizer/Scheduling/SchedulePoller.cs`: Schedule polling and advance.
- `src/Atomizer/Scheduling/ScheduleProcessor.cs`: Occurrence fan-out to jobs.
- `src/Atomizer/Storage/InMemoryStorage.cs`: In-memory backend.
- `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs`: EF Core backend.
- `src/Atomizer.EntityFrameworkCore/Providers/RelationalProviderCache.cs`: Provider detection and raw-SQL routing.

**Domain Models:**
- `src/Atomizer/Models/AtomizerJob.cs`: Job aggregate + `AtomizerJobStatus` enum.
- `src/Atomizer/Models/AtomizerSchedule.cs`: Recurring schedule aggregate + `MisfirePolicy` enum.
- `src/Atomizer/Models/AtomizerJobError.cs`: Per-attempt failure record.
- `src/Atomizer/Models/ValueObjects/RetryStrategy.cs`: Retry behavior (factories: `Default`, `None`, `Fixed`, `Intervals`, `Exponential`).
- `src/Atomizer/Models/ValueObjects/Schedule.cs`: Cron value object (`EverySecond`, `EveryMinute`, `Hourly`, `Daily`, `Weekly`, `Monthly`, `Cron(...)`).
- `src/Atomizer/Models/ValueObjects/QueueKey.cs`, `JobKey.cs`, `LeaseToken.cs`, `WorkerId.cs`.

**Testing:**
- `tests/Atomizer.Tests/Atomizer.Tests.csproj`: xUnit v3 unit tests.
- `tests/Atomizer.EntityFrameworkCore.Tests/Atomizer.EntityFrameworkCore.Tests.csproj`: Testcontainers-backed integration tests.
- `tests/Atomizer.Tests.Utilities/Atomizer.Tests.Utilities.csproj`: Shared helpers (`FakeDataFactory`, `NonPublicSpy`, `TestableLogger`, sample jobs).
- `tests/Atomizer.EntityFrameworkCore.Tests/Fixtures/BaseDatabaseFixture.cs`: Base per-provider container fixture.

## Naming Conventions

**Files:**
- Pascal-case, one public type per file: `AtomizerJob.cs`, `QueuePump.cs`, `DefaultJobDispatcher.cs`.
- Interfaces prefixed with `I`: `IAtomizerClient.cs`, `IAtomizerStorage.cs`.
- Default / internal implementations of an interface prefixed with `Default`: `DefaultJobDispatcher.cs`, `DefaultJobSerializer.cs`, `DefaultJobTypeResolver.cs`.
- Factory types suffixed with `Factory`: `QueuePumpFactory.cs`, `JobWorkerFactory.cs`, `JobProcessorFactory.cs`, `InMemoryLeasingScopeFactory.cs`.
- Hosted services suffixed with `Service`: `AtomizerQueueService.cs`, `AtomizerSchedulerService.cs`.
- Options classes suffixed with `Options`: `AtomizerOptions.cs`, `QueueOptions.cs`, `SchedulingOptions.cs`, `JobStorageOptions.cs`, `LeasingScopeOptions.cs`, `AtomizerProcessingOptions.cs`, `InMemoryJobStorageOptions.cs`, `EntityFrameworkCoreJobStorageOptions.cs`.
- Exceptions suffixed with `Exception`: `InvalidAtomizerConfigurationException.cs`, `PayloadSerializationException.cs`.
- Entity classes (EF Core) suffixed with `Entity`: `AtomizerJobEntity.cs`. Entity configurations suffixed with `EntityConfiguration`: `AtomizerJobEntityConfiguration.cs`.
- Test classes suffixed with `Tests`: `{Type}Tests` (e.g., `AtomizerJobTests`).

**Directories:**
- Pascal-case, semantic (by responsibility, not by pattern): `Abstractions`, `Core`, `Configuration`, `Models`, `Processing`, `Scheduling`, `Storage`, `Exceptions`, `Providers`, `Entities`, `Configurations`, `Extensions`.
- Test project directories mirror `src/` subfolders.

**Namespaces:**
- Root namespace for the core library is `Atomizer`; subdirectories typically use `Atomizer.<Folder>` (e.g., `Atomizer.Core`, `Atomizer.Processing`, `Atomizer.Scheduling`, `Atomizer.Storage`, `Atomizer.Abstractions`, `Atomizer.Exceptions`, `Atomizer.Models.Base`).
- Public-facing consumer types intentionally live in `namespace Atomizer;` even when in subfolders (uses `// ReSharper disable once CheckNamespace`). Examples: `IAtomizerClient`, `AtomizerJob`, `AtomizerSchedule`, `QueueKey`, `JobKey`, `Schedule`, `RetryStrategy`, `EnqueueOptions`, `RecurringOptions`, `AtomizerOptions`, `ServiceCollectionExtensions`.
- EF Core assembly uses `Atomizer.EntityFrameworkCore` and nested namespaces.

**Members:**
- Public members: Pascal-case (`EnqueueAsync`, `QueueKey`, `BatchSize`).
- Private fields: `_camelCase` with leading underscore (`_clock`, `_serviceScopeFactory`, `_channel`).
- Constants: Pascal-case (`MaxReadAttempts`).
- Enum members: Pascal-case, explicitly numbered when persisted (`Pending = 1`, `Processing = 2`, `Completed = 3`, `Failed = 4` in `AtomizerJobStatus`; same for `AtomizerEntityJobStatus`).

## Where to Add New Code

**New consumer-facing abstraction:**
- Interface: `src/Atomizer/Abstractions/IAtomizer<Thing>.cs`.
- Default implementation: `src/Atomizer/Core/Default<Thing>.cs` (or similarly named).
- DI registration: `src/Atomizer/Configuration/ServiceCollectionExtensions.cs`.

**New queue pipeline component:**
- Implementation: `src/Atomizer/Processing/{Component}.cs` with `internal interface I{Component}` at the top and internal `sealed class` below.
- Factory (if instance-per-queue or per-job): `src/Atomizer/Processing/{Component}Factory.cs`.
- DI registration: `src/Atomizer/Configuration/ServiceCollectionExtensions.AddAtomizerProcessing`.

**New scheduling feature:**
- Implementation: `src/Atomizer/Scheduling/{Component}.cs`.
- DI registration: `src/Atomizer/Configuration/ServiceCollectionExtensions.AddAtomizerProcessing`.
- Options: `src/Atomizer/Configuration/SchedulingOptions.cs`.

**New domain state / invariant:**
- Aggregate: add method to `src/Atomizer/Models/AtomizerJob.cs` or `src/Atomizer/Models/AtomizerSchedule.cs`. Never mutate `Status`/`VisibleAt`/`LeaseToken` outside the aggregate; throw `InvalidOperationException` for illegal transitions.
- New value object: `src/Atomizer/Models/ValueObjects/{Name}.cs` deriving from `Atomizer.Models.Base.ValueObject` and implementing `GetEqualityValues()`.
- New exception: `src/Atomizer/Exceptions/Invalid{Thing}Exception.cs`.

**New storage backend:**
- Implement `IAtomizerStorage` in its own assembly/directory (follow the `Atomizer.EntityFrameworkCore` pattern).
- Provide an `IAtomizerLeasingScopeFactory` (or reuse `NoopLeasingScopeFactory`).
- Expose a fluent `UseXxxStorage` extension on `AtomizerOptions` that sets `JobStorageOptions` and `LeasingScopeOptions` (see `src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs:11`).

**New EF Core provider:**
- Extend `enum DatabaseProvider` in `src/Atomizer.EntityFrameworkCore/Providers/DatabaseProvider.cs`.
- Add provider-name detection in `RelationalProviderCache.DetectProvider` (`src/Atomizer.EntityFrameworkCore/Providers/RelationalProviderCache.cs:73`) and mark it supported in `DetermineSupportedProvider`.
- Implement `IDatabaseProviderSql` in `src/Atomizer.EntityFrameworkCore/Providers/Sql/{Provider}Provider.cs` with raw SQL that atomically leases a batch (use `FOR NO KEY UPDATE SKIP LOCKED` / `READPAST` / equivalent).
- Branch on the new provider in `RelationalProviderCache.CreateRawSqlProvider`.
- Add a matching fixture under `tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/{Provider}/` and a sample DbContext in `samples/Atomizer.EFCore.Example/Data/{Provider}/`.

**New consumer handler (in a consuming app):**
- Implement `IAtomizerJob<TPayload>` anywhere in the assembly; register via `AtomizerOptions.AddHandlersFrom<TMarker>()`. Handlers are registered as `Scoped` automatically (`src/Atomizer/Configuration/AtomizerOptions.cs:64`).

**Unit tests:**
- Mirror the `src/Atomizer/` folder under `tests/Atomizer.Tests/`. Test class suffix `Tests`, method name pattern `{Method}_When{Scenario}_Should{ExpectedBehavior}`.
- Reuse shared helpers from `tests/Atomizer.Tests.Utilities/` (`FakeDataFactory`, `NonPublicSpy`, `TestableLogger`, `TestJobs/`, `Stubs/`).

**EF Core integration tests:**
- Add tests under `tests/Atomizer.EntityFrameworkCore.Tests/Storage/` using a `BaseDatabaseFixture<TDbContext>` subtype. Per-provider DbContexts live in `tests/Atomizer.EntityFrameworkCore.Tests/TestSetup/{Provider}/`.

**New options field:**
- Extend the appropriate `*Options` class under `src/Atomizer/Configuration/` (or `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreJobStorageOptions.cs`).
- Validate in `AtomizerOptions.AddQueue` / `ConfigureScheduling` / `ServiceCollectionExtensions`, throwing `InvalidAtomizerConfigurationException` for invalid combinations.

## Special Directories

**`src/*/bin/` and `src/*/obj/`:**
- Purpose: Build output.
- Generated: Yes.
- Committed: No (standard `.gitignore`).

**`src/*/packages.lock.json`:**
- Purpose: NuGet restore lockfile (`RestorePackagesWithLockFile=true` in `Atomizer.csproj`).
- Generated: Yes (via `dotnet restore`).
- Committed: Yes.

**`.planning/codebase/`:**
- Purpose: Output folder for codebase-mapping documents (ARCHITECTURE.md, STRUCTURE.md, etc.) consumed by downstream GSD commands.
- Generated: Yes (via this agent).
- Committed: Yes.

**`.github/workflows/`:**
- Purpose: GitHub Actions CI/CD.
- Committed: Yes.

**`.idea/`, `.omc/`, `.omx/`:**
- Purpose: IDE / orchestration tooling state.
- Generated: Yes.
- Committed: Mixed (tooling-dependent).

**`samples/`:**
- Purpose: Reference applications demonstrating integration. Not published to NuGet.
- Committed: Yes.

**`tests/`:**
- Purpose: Test projects (xUnit v3). Not published to NuGet.
- Committed: Yes.

---

*Structure analysis: 2026-05-03*
