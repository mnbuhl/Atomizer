<!-- refreshed: 2026-05-03 -->
# Architecture

**Analysis Date:** 2026-05-03

## System Overview

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│                    Consumer Application (ASP.NET Core host)                  │
│                                                                              │
│   IAtomizerClient                         IAtomizerJob<TPayload>             │
│   `src/Atomizer/Abstractions/IAtomizerClient.cs`  (user-implemented)         │
│     │                                               ▲                        │
│     │ EnqueueAsync / ScheduleAsync /                │ HandleAsync(payload)   │
│     │ ScheduleRecurringAsync                        │                        │
│     ▼                                               │                        │
│   AtomizerClient                                    │                        │
│   `src/Atomizer/Core/AtomizerClient.cs`             │                        │
└────────┬────────────────────────────────────────────┴────────────────────────┘
         │ writes job/schedule via IAtomizerServiceScopeFactory
         ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│                             IAtomizerStorage                                 │
│   `src/Atomizer/Abstractions/IAtomizerStorage.cs`                            │
│   ┌──────────────────────────┐          ┌──────────────────────────────┐     │
│   │ InMemoryStorage          │          │ EntityFrameworkCoreStorage   │     │
│   │ `src/Atomizer/Storage/   │          │ `src/Atomizer.Entity         │     │
│   │  InMemoryStorage.cs`     │          │  FrameworkCore/Storage/...`  │     │
│   │ Concurrent dictionaries  │          │ DbContext + provider-        │     │
│   │ + per-queue semaphores   │          │ specific raw SQL            │      │
│   └──────────────────────────┘          └──────────────────────────────┘     │
└──────────────────────────────────────────────────────────────────────────────┘
         ▲                                              ▲
         │ GetDueJobs / UpdateJobs / ReleaseLeased      │ GetDueSchedules /
         │                                              │ InsertAsync
         │                                              │
┌────────┴──────────────────────────┐   ┌───────────────┴──────────────────────┐
│     Queue Processing Pipeline     │   │      Scheduler Pipeline              │
│  `src/Atomizer/Processing/`       │   │  `src/Atomizer/Scheduling/`          │
│                                   │   │                                      │
│  AtomizerQueueService             │   │  AtomizerSchedulerService            │
│   (IHostedService)                │   │   (IHostedService)                   │
│     └── QueueCoordinator          │   │     └── Scheduler                    │
│          └── QueuePump  (× queue) │   │          └── SchedulePoller          │
│               ├── QueuePoller     │   │               └── ScheduleProcessor  │
│               └── JobWorker(s)    │   │                    (enqueues jobs)   │
│                    └── JobProcessor                                          │
│                         └── IAtomizerJobDispatcher                           │
│                              └── DefaultJobDispatcher                        │
│                                   (reflects IAtomizerJob<T> handler)         │
└──────────────────────────────────────────────────────────────────────────────┘
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

**Overall:** Hosted background services + producer/consumer pipeline over a pluggable storage-backed work queue, with a separate schedule-fanout loop. Domain state transitions are enforced on rich aggregate roots (`AtomizerJob`, `AtomizerSchedule`).

**Key Characteristics:**
- Layered separation between public API (`IAtomizerClient`), domain (`AtomizerJob`, `AtomizerSchedule`, value objects), infrastructure (`IAtomizerStorage`, `IAtomizerLeasingScopeFactory`), and execution (`Processing/`, `Scheduling/`).
- Pluggable storage via `JobStorageOptions` (factory + `ServiceLifetime`) and pluggable locking via `LeasingScopeOptions`.
- Producer/consumer within a queue uses `System.Threading.Channels.Channel<AtomizerJob>` bounded to `DegreeOfParallelism × BatchSize`.
- Every job is handled in a fresh `IServiceScope`, so handlers registered as `Scoped` see per-job lifetimes (see `DefaultJobDispatcher`).
- Two cancellation tokens (I/O vs. execution) are threaded through the pipeline for two-phase graceful shutdown.
- Distributed coordination is an abstraction (`IAtomizerLeasingScopeFactory`); the EF Core backend layers it on top of `FOR NO KEY UPDATE SKIP LOCKED` raw SQL.

## Layers

**Public API (assembly `Atomizer`, root namespace `Atomizer`):**
- Purpose: Surface consumer-facing types (`IAtomizerClient`, `EnqueueOptions`, `RecurringOptions`, `AtomizerJob`, `AtomizerSchedule`, value objects, `JobContext`, enums).
- Location: `src/Atomizer/` (types live at `namespace Atomizer` via `// ReSharper disable once CheckNamespace` even when in subfolders).
- Contains: Abstractions, domain types, options, exceptions.
- Depends on: `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`, `Cronos`, `System.Text.Json`, `System.Threading.Channels` (all `[6.0.0, )`).
- Used by: Consumer code and the `Atomizer.EntityFrameworkCore` package.

**Abstractions layer:**
- Purpose: Interfaces that enable pluggable storage, locking, serialization, and type resolution.
- Location: `src/Atomizer/Abstractions/`.
- Key files: `IAtomizerClient.cs`, `IAtomizerStorage.cs`, `IAtomizerJob.cs`, `IAtomizerJobSerializer.cs`, `IAtomizerServiceScope.cs`, `IAtomizerLeasingScope.cs`, `IAtomizerLeasingScopeFactory.cs`.

**Core (infrastructure) layer:**
- Purpose: Default implementations of the abstractions, plus glue with `IServiceProvider`.
- Location: `src/Atomizer/Core/`.
- Contains: `AtomizerClient`, `AtomizerClock`, `AtomizerRuntimeIdentity`, `DefaultJobDispatcher` (+ `IAtomizerJobDispatcher`), `DefaultJobSerializer`, `DefaultJobTypeResolver` (+ `IAtomizerJobTypeResolver`), `NoopLeasingScopeFactory`, `ServiceProviderServiceScope(Factory)`.
- Depends on: Abstractions, Exceptions, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`.

**Domain layer:**
- Purpose: Aggregates and value objects with enforced state transitions.
- Location: `src/Atomizer/Models/`.
- Files: `AtomizerJob.cs`, `AtomizerSchedule.cs`, `AtomizerJobError.cs`, `Base/Model.cs`, `Base/ValueObject.cs`, `ValueObjects/*.cs`.
- Rule: State changes go through domain methods (`Lease`, `Attempt`, `MarkAsCompleted`, `MarkAsFailed`, `Reschedule`, `Release`, `Disable`, `UpdateNextOccurence`).

**Configuration layer:**
- Purpose: Options classes + `IServiceCollection` extensions.
- Location: `src/Atomizer/Configuration/`.
- Files: `AtomizerOptions.cs`, `AtomizerOptionsExtensions.cs` (in-memory storage plug-in), `AtomizerProcessingOptions.cs`, `JobStorageOptions.cs`, `LeasingScopeOptions.cs`, `QueueOptions.cs`, `SchedulingOptions.cs`, `ServiceCollectionExtensions.cs`.
- Entry points: `services.AddAtomizer(...)` and `services.AddAtomizerProcessing(...)`.

**Processing layer (queue workers):**
- Purpose: Poll storage, lease batches, fan out to workers, dispatch handlers.
- Location: `src/Atomizer/Processing/`.
- Depends on: Abstractions, Core (`IAtomizerClock`, `IAtomizerJobDispatcher`, `AtomizerRuntimeIdentity`), Configuration (`AtomizerOptions`, `QueueOptions`), `System.Threading.Channels`, `Microsoft.Extensions.Hosting.Abstractions`.

**Scheduling layer:**
- Purpose: Produce jobs from recurring schedules.
- Location: `src/Atomizer/Scheduling/`.
- Depends on: Abstractions, Core, Configuration (`SchedulingOptions`), `Cronos`.

**Storage layer (in-memory):**
- Location: `src/Atomizer/Storage/`.
- Files: `InMemoryStorage.cs`, `InMemoryLeasingScopeFactory.cs`, `InMemoryJobStorageOptions.cs`.

**EF Core storage assembly (`Atomizer.EntityFrameworkCore`):**
- Purpose: Relational storage and locking backend.
- Location: `src/Atomizer.EntityFrameworkCore/`.
- Structure:
  - `Entities/` — plain entity classes (`AtomizerJobEntity`, `AtomizerJobErrorEntity`, `AtomizerScheduleEntity`) and mappers to/from domain types.
  - `Configurations/` — EF `IEntityTypeConfiguration<T>` implementations.
  - `Providers/` — `DatabaseProvider` enum, `RelationalProviderCache`, `EntityMap`, `IDatabaseProviderSql`, `Sql/{SqlServer,PostgreSql,MySql}Provider.cs`.
  - `Storage/` — `EntityFrameworkCoreStorage<TDbContext>`, `DatabaseTransactionLeasingScope(Factory)`, `EntityFrameworkCoreJobStorageOptions`.
  - `Extensions/` — `AtomizerOptionsExtensions.UseEntityFrameworkCoreStorage<TDbContext>`, `ModelBuilderExtensions.AddAtomizerEntities`.
- Depends on: `Atomizer` (core), `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.EntityFrameworkCore`.

## Data Flow

### Enqueue Path (`IAtomizerClient.EnqueueAsync` / `ScheduleAsync`)

1. Caller invokes `EnqueueAsync<TPayload>` (`src/Atomizer/Abstractions/IAtomizerClient.cs:6`). `ScheduleAsync` routes to the same internal path with an explicit `runAt`.
2. `AtomizerClient.EnqueueInternalAsync` serializes the payload via `IAtomizerJobSerializer.Serialize` (`src/Atomizer/Core/AtomizerClient.cs:87`) and builds an `AtomizerJob` with status `Pending` via `AtomizerJob.Create` (`src/Atomizer/Models/AtomizerJob.cs:24`).
3. A fresh service scope is created from `IAtomizerServiceScopeFactory.CreateScope`, which resolves `IAtomizerStorage` (and `IAtomizerLeasingScopeFactory`) from DI (`src/Atomizer/Core/ServiceProviderServiceScope.cs:22`).
4. `storage.InsertAsync(job, ct)` writes the job. For EF Core this checks `IdempotencyKey` first (`src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:40`). For in-memory it indexes the job per queue and evicts terminal jobs over retention (`src/Atomizer/Storage/InMemoryStorage.cs:27`).
5. Returns the job `Guid` to the caller.

### Recurring Schedule Path (`IAtomizerClient.ScheduleRecurringAsync`)

1. Caller invokes `ScheduleRecurringAsync<TPayload>(payload, JobKey, Schedule, configure)` (`src/Atomizer/Core/AtomizerClient.cs:51`).
2. `AtomizerSchedule.Create` computes `NextRunAt` from the `Schedule` cron via `Cronos.CronExpression.GetNextOccurrence` (`src/Atomizer/Models/AtomizerSchedule.cs:56`).
3. `storage.UpsertScheduleAsync(schedule, ct)` inserts or updates by `JobKey` (`src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:155`; in-memory at `src/Atomizer/Storage/InMemoryStorage.cs:155`).

### Queue Processing Path (inbound run-loop)

1. `AtomizerQueueService.ExecuteAsync` waits `StartupDelay` then calls `QueueCoordinator.Start(stoppingToken)` (`src/Atomizer/Processing/AtomizerQueueService.cs:23`).
2. `QueueCoordinator.Start` creates one `QueuePump` per configured queue via `IQueuePumpFactory` (`src/Atomizer/Processing/QueueCoordinator.cs:30`).
3. `QueuePump` constructor allocates `Channel.CreateBounded<AtomizerJob>(DegreeOfParallelism × BatchSize)` and derives a `LeaseToken` formatted `{InstanceId}:*:{QueueKey}:*:{N32guid}` (`src/Atomizer/Processing/QueuePump.cs:48`).
4. `QueuePump.Start` runs the poller task and `DegreeOfParallelism` worker tasks (`src/Atomizer/Processing/QueuePump.cs:71`).
5. `QueuePoller.RunAsync` loop: on each `TickInterval`, if `now - lastCheck >= StorageCheckInterval` and the channel has fewer than `DegreeOfParallelism` items queued, it creates a service scope, acquires a leasing scope via `IAtomizerLeasingScopeFactory.CreateScopeAsync(queue.QueueKey, queue.VisibilityTimeout, ct)`, calls `storage.GetDueJobsAsync`, mutates each job with `AtomizerJob.Lease(leaseToken, now, visibilityTimeout)`, persists via `storage.UpdateJobsAsync`, and writes the jobs to the channel (`src/Atomizer/Processing/QueuePoller.cs:34`).
6. Each `JobWorker` reads a job from the channel (with bounded read-retry on transient failures) and asks `IJobProcessorFactory` for a `JobProcessor` (`src/Atomizer/Processing/JobWorker.cs:27`).
7. `JobProcessor.ProcessAsync` calls `job.Attempt()`, then `_dispatcher.DispatchAsync(job, executionToken)` (`src/Atomizer/Processing/JobProcessor.cs:32`).
8. `DefaultJobDispatcher.DispatchAsync` resolves the handler type via `IAtomizerJobTypeResolver`, deserializes the payload, creates a fresh `IServiceScope`, resolves `IAtomizerJob<TPayload>`, and calls `HandleAsync` via reflection; `TargetInvocationException` is unwrapped with `ExceptionDispatchInfo.Capture(...).Throw()` so retry logic sees the real exception (`src/Atomizer/Core/DefaultJobDispatcher.cs:34`).
9. On success, `JobProcessor` calls `job.MarkAsCompleted(now)` and persists. On exception, `HandleFailureAsync` appends an `AtomizerJobError`, then either `job.Reschedule(nextVisibleAt, now)` using `RetryStrategy.GetRetryInterval(attempts)` or `job.MarkAsFailed(now)` once `ShouldRetry(attempts)` is false (`src/Atomizer/Processing/JobProcessor.cs:73`).

### Scheduling Path (recurring fan-out)

1. `AtomizerSchedulerService.ExecuteAsync` waits `StartupDelay` then calls `Scheduler.Start` (`src/Atomizer/Scheduling/AtomizerSchedulerService.cs:23`).
2. `Scheduler.Start` runs a single `SchedulePoller.RunAsync(ioToken, executionToken)` task (`src/Atomizer/Scheduling/Scheduler.cs:27`).
3. `SchedulePoller.RunAsync` loop: on each `TickInterval`, if `now - lastCheck >= StorageCheckInterval`, acquire a leasing scope on the reserved `QueueKey.Scheduler` (`src/Atomizer/Models/ValueObjects/QueueKey.cs:9`), compute `horizon = now + ScheduleLeadTime`, call `storage.GetDueSchedulesAsync(horizon, ct)`, and for each due schedule invoke `_scheduleProcessor.ProcessAsync(schedule, horizon, ct)` then `schedule.UpdateNextOccurence(horizon, now)`; finally `storage.UpdateSchedulesAsync` persists the advance (`src/Atomizer/Scheduling/SchedulePoller.cs:38`).
4. `ScheduleProcessor.ProcessAsync` calls `schedule.GetOccurrences(horizon)` which applies the `MisfirePolicy` (`Ignore` / `ExecuteNow` / `CatchUp` bounded by `MaxCatchUp`) — see `src/Atomizer/Models/AtomizerSchedule.cs:62`. For each occurrence it builds an `AtomizerJob` with idempotency key `"{JobKey}:*:{occurrence:O}"` and calls `storage.InsertAsync` (`src/Atomizer/Scheduling/ScheduleProcessor.cs:29`).
5. From here the job flows through the normal Queue Processing Path.

### Shutdown Path (`AtomizerQueueService.StopAsync`)

1. `AtomizerQueueService.StopAsync` calls `QueueCoordinator.StopAsync(GracefulShutdownTimeout, ct)` (`src/Atomizer/Processing/AtomizerQueueService.cs:34`).
2. `QueueCoordinator.StopAsync` awaits `StopAsync` on every pump in parallel (`src/Atomizer/Processing/QueueCoordinator.cs:41`).
3. Each `QueuePump.StopAsync`:
   - Cancels `_ioCts` and calls `_channel.Writer.TryComplete()` — poller stops fetching; workers stop reading (`src/Atomizer/Processing/QueuePump.cs:94`).
   - `await Task.WhenAny(Task.WhenAll(workers), Task.Delay(gracePeriod))`.
   - If the grace period fires first, cancels `_executionCts` to interrupt running handlers.
   - Always calls `storage.ReleaseLeasedAsync(_leaseToken, now)` with a 5-second timeout to push in-flight jobs back to `Pending` (they reappear after their `VisibleAt` is cleared) (`src/Atomizer/Processing/QueuePump.cs:123`).

**State management:**
- Jobs/schedules: persisted in `IAtomizerStorage`; in-memory state lives in `ConcurrentDictionary`s inside `InMemoryStorage`.
- Runtime: `AtomizerOptions` is registered as a singleton; `AtomizerRuntimeIdentity` is a singleton per process.
- Channels are per `QueuePump` (one per queue) and do not cross queue boundaries.

## Key Abstractions

**`IAtomizerStorage` (pluggable backend):**
- Purpose: Insert/update jobs and schedules, atomically lease due jobs, release leases by token, fetch due schedules.
- Examples: `src/Atomizer/Storage/InMemoryStorage.cs`, `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs`.
- Pattern: Strategy — registered via `JobStorageOptions(Func<IServiceProvider, IAtomizerStorage>, ServiceLifetime)` and wired in `ServiceCollectionExtensions.AddAtomizer` (`src/Atomizer/Configuration/ServiceCollectionExtensions.cs:42`).

**`IAtomizerLeasingScopeFactory` (pluggable distributed lock):**
- Purpose: Per-queue lock abstraction used exactly around `GetDueJobsAsync → UpdateJobsAsync` and around schedule polling.
- Examples: `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs`, `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs`, `src/Atomizer/Core/NoopLeasingScopeFactory.cs`.
- Pattern: Strategy + scope/disposable. If `Acquired == false`, the poller skips this tick.

**`IAtomizerJob<TPayload>` (consumer handler):**
- Purpose: Marker/interface users implement per payload type. Registered from an assembly via `AtomizerOptions.AddHandlersFrom<TMarker>()` (lifetime: `Scoped`) — see `src/Atomizer/Configuration/AtomizerOptions.cs:44`.

**`IAtomizerJobDispatcher` (dispatch chain):**
- Purpose: Decouples `JobProcessor` from reflection and DI wiring. Implemented by `DefaultJobDispatcher` which creates a fresh `IServiceScope` per job.

**Aggregate roots (`AtomizerJob`, `AtomizerSchedule`):**
- Purpose: State transitions are enforced by domain methods; status is an integer enum persisted as-is.
- Pattern: Rich domain model (methods throw `InvalidOperationException` when state is wrong — see `AtomizerJob.Lease`, `Attempt`, `Release`).

**Value objects (`ValueObject` base in `src/Atomizer/Models/Base/ValueObject.cs`):**
- Equality via `GetEqualityValues()` (sequence compare).
- Concrete types: `QueueKey`, `JobKey`, `LeaseToken`, `RetryStrategy`, `Schedule`, `WorkerId` — all in `src/Atomizer/Models/ValueObjects/`.
- Most support implicit `string ⇄ T` conversions; length/format guards throw dedicated exceptions from `src/Atomizer/Exceptions/`.

**`IAtomizerServiceScope` (`src/Atomizer/Abstractions/IAtomizerServiceScope.cs`):**
- A thin wrapper around `IServiceScope` exposing `Storage` and `LeasingScopeFactory` so internal services can resolve the storage+lock pair without reaching into `IServiceProvider`.

## Entry Points

**`services.AddAtomizer(Action<AtomizerOptions>)` (`src/Atomizer/Configuration/ServiceCollectionExtensions.cs:13`):**
- Triggers: Application startup (`Program.cs` / DI bootstrap).
- Responsibilities:
  - Validates `JobStorageOptions` is set; throws `InvalidAtomizerConfigurationException` otherwise.
  - Ensures a default queue (`QueueKey.Default`) exists when none is configured.
  - Registers: `AtomizerOptions` (singleton), all handlers from `options.Handlers`, `IAtomizerClient` (singleton), `IAtomizerClock` (singleton), `IAtomizerJobTypeResolver` (singleton), `IAtomizerJobDispatcher` (singleton), `IAtomizerJobSerializer` (singleton), `IAtomizerServiceScopeFactory` (singleton), plus `IAtomizerStorage` and `IAtomizerLeasingScopeFactory` with the lifetimes declared in their respective options.

**`services.AddAtomizerProcessing(Action<AtomizerProcessingOptions>)` (`src/Atomizer/Configuration/ServiceCollectionExtensions.cs:61`):**
- Triggers: Application startup (opt-in on hosts that actually execute jobs).
- Responsibilities:
  - Registers `AtomizerRuntimeIdentity` (singleton).
  - Hosts `AtomizerQueueService` and `AtomizerSchedulerService` (`AddHostedService`).
  - Registers pipeline components: `IQueueCoordinator`, `IQueuePumpFactory`, `IQueuePoller`, `IJobWorkerFactory`, `IJobProcessorFactory`, `IScheduler`, `ISchedulePoller`, `IScheduleProcessor`.
  - Validates `StartupDelay >= TimeSpan.Zero`.

**`AtomizerOptions.UseInMemoryStorage(Action<InMemoryJobStorageOptions>)` (`src/Atomizer/Configuration/AtomizerOptionsExtensions.cs:10`):**
- Installs `InMemoryStorage` and `InMemoryLeasingScopeFactory`.

**`AtomizerOptions.UseEntityFrameworkCoreStorage<TDbContext>(Action<EntityFrameworkCoreJobStorageOptions>)` (`src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs:11`):**
- Installs `EntityFrameworkCoreStorage<TDbContext>` and `DatabaseTransactionLeasingScopeFactory<TDbContext>` (both `Scoped`).

**`ModelBuilder.AddAtomizerEntities(string? schema = "Atomizer")` (`src/Atomizer.EntityFrameworkCore/Extensions/ModelBuilderExtensions.cs:8`):**
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

**What happens:** Code sets `job.Status = AtomizerJobStatus.Processing` or `job.VisibleAt = ...` instead of calling the domain method.
**Why it's wrong:** The domain methods (`Lease`, `Attempt`, `Release`, `MarkAsCompleted`, `MarkAsFailed`, `Reschedule`) throw `InvalidOperationException` if the current state is illegal and keep `UpdatedAt`/`LeaseToken`/`VisibleAt` consistent. Direct mutation bypasses the guard rails and silently corrupts state.
**Do this instead:** Use the domain method — e.g., `job.Lease(leaseToken, now, visibilityTimeout)` (`src/Atomizer/Models/AtomizerJob.cs:52`) or `job.MarkAsCompleted(now)` (`src/Atomizer/Models/AtomizerJob.cs:88`).

### Resolving handlers outside a DI scope

**What happens:** Resolving `IAtomizerJob<TPayload>` from a singleton/root `IServiceProvider` instead of a fresh scope.
**Why it's wrong:** Handlers are registered as `Scoped` (`src/Atomizer/Configuration/AtomizerOptions.cs:64`). Without a per-job scope, `DbContext` and other scoped services leak across jobs.
**Do this instead:** Always dispatch through `IAtomizerJobDispatcher`. `DefaultJobDispatcher` creates a fresh `scopeFactory.CreateScope()` per job (`src/Atomizer/Core/DefaultJobDispatcher.cs:57`).

### Skipping the leasing scope when polling storage

**What happens:** New polling code calls `storage.GetDueJobsAsync` without first acquiring an `IAtomizerLeasingScope`.
**Why it's wrong:** Without the leasing scope the EF Core backend cannot wrap the read + `UpdateJobsAsync` in a single `ReadCommitted` transaction; concurrent pumps on multiple instances can lease the same rows. The in-memory implementation similarly relies on the per-queue semaphore.
**Do this instead:** Follow the pattern in `QueuePoller.RunAsync` — resolve `scope.LeasingScopeFactory`, `await using` the `IAtomizerLeasingScope`, check `leasingScope.Acquired`, then call storage (`src/Atomizer/Processing/QueuePoller.cs:54`).

### Throwing `TargetInvocationException` out of a handler

**What happens:** Reflection-based dispatch re-throws `TargetInvocationException` verbatim.
**Why it's wrong:** `JobProcessor.HandleFailureAsync` inspects the actual thrown exception to record it in `AtomizerJobError` and to decide retry eligibility. A wrapped exception hides the real error.
**Do this instead:** Unwrap with `ExceptionDispatchInfo.Capture(ex.InnerException!).Throw()` as `DefaultJobDispatcher` does (`src/Atomizer/Core/DefaultJobDispatcher.cs:84`).

### Using `DateTime.UtcNow` inside pipeline code

**What happens:** A new service reads `DateTime.UtcNow` / `DateTimeOffset.UtcNow` directly.
**Why it's wrong:** Tests substitute `IAtomizerClock` with a fake; direct clock reads break deterministic tests and misfire logic.
**Do this instead:** Inject `IAtomizerClock` and read `_clock.UtcNow`.

### Adding a new EF Core provider without updating `RelationalProviderCache`

**What happens:** A consumer plugs in a new EF provider (e.g. CockroachDB) and enables `AllowUnsafeProviderFallback`.
**Why it's wrong:** Fallback LINQ queries do not use `FOR NO KEY UPDATE SKIP LOCKED` or an equivalent — two replicas can lease the same job.
**Do this instead:** Add a new `DatabaseProvider` entry, detection string in `RelationalProviderCache.DetectProvider`, a matching `IDatabaseProviderSql` implementation in `src/Atomizer.EntityFrameworkCore/Providers/Sql/`, and branch on it in `CreateRawSqlProvider` (`src/Atomizer.EntityFrameworkCore/Providers/RelationalProviderCache.cs:64`).

## Error Handling

**Strategy:** Exceptions within a handler are caught by `JobProcessor.ProcessAsync` and converted into retry / fail decisions via `RetryStrategy.ShouldRetry(attempts)`. Infrastructure-level exceptions in poller/worker loops are logged and the loop continues; `OperationCanceledException` is filtered by the owning token.

**Patterns:**
- Typed exceptions for configuration/domain invariants: `InvalidAtomizerConfigurationException`, `InvalidJobKeyException`, `InvalidLeaseTokenException`, `InvalidQueueKeyException`, `InvalidRetryStrategyException`, `JobResolverException`, `PayloadSerializationException` — all in `src/Atomizer/Exceptions/`.
- Retry record: `AtomizerJobError.Create(jobId, timestamp, attempt, exception, instanceId)` is appended to `job.Errors` on each failed attempt (`src/Atomizer/Processing/JobProcessor.cs:79`).
- `RetryStrategy` factory validates inputs and throws `InvalidRetryStrategyException` on invalid delays / `MaxAttempts < 1` / `Exponent <= 1.0`.
- EF Core `UpsertScheduleAsync` currently catches `DbUpdateException` and logs (`src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs:178`); a `@todo` notes moving to optimistic concurrency.

## Cross-Cutting Concerns

**Logging:** `Microsoft.Extensions.Logging` (`ILogger<T>`) everywhere. Workers and processors build named loggers including `WorkerId` / `processorId` (`src/Atomizer/Processing/JobWorkerFactory.cs:34`, `src/Atomizer/Processing/JobProcessorFactory.cs:40`). Standard levels used: `Debug` for poll-loop/lease bookkeeping, `Information` for queue lifecycle and successful job completion, `Warning` for retries and graceful-shutdown timeout, `Error` for failures.

**Validation:** Performed in `AtomizerOptions.AddQueue` / `ConfigureScheduling` and in value-object constructors; `ServiceCollectionExtensions.AddAtomizer` enforces that a storage backend is set.

**Authentication:** Out of scope for the library — there is no inbound request surface.

**Clock:** `IAtomizerClock` is injected anywhere time is read or compared.

**Identity:** `AtomizerRuntimeIdentity.InstanceId` is embedded in every `LeaseToken` and `WorkerId`, enabling multi-instance differentiation in logs and in `ReleaseLeasedAsync` queries.

**Serialization:** Single cached `JsonSerializerOptions` with `JsonSerializerDefaults.Web` inside `DefaultJobSerializer` (`src/Atomizer/Core/DefaultJobSerializer.cs:10`).

---

*Architecture analysis: 2026-05-03*
