# Phase 5: Cleanup and Versioning - Pattern Map

**Mapped:** 2026-05-03
**Files analyzed:** 27
**Analogs found:** 27 / 27

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScope.cs` | utility | request-response | `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs` | role-match |
| `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs` | — DELETE — | — | — | — |
| `src/Atomizer/Atomizer.csproj` | config | — | `src/Atomizer.EntityFrameworkCore/Atomizer.EntityFrameworkCore.csproj` | exact |
| `src/Atomizer.EntityFrameworkCore/Atomizer.EntityFrameworkCore.csproj` | config | — | `src/Atomizer/Atomizer.csproj` | exact |
| `src/Atomizer/Abstractions/IAtomizerClient.cs` | interface | request-response | `src/Atomizer/Abstractions/IAtomizerStorage.cs` | exact |
| `src/Atomizer/Abstractions/IAtomizerStorage.cs` | interface | CRUD | self (already documented) | exact |
| `src/Atomizer/Abstractions/IAtomizerJob.cs` | interface | request-response | `src/Atomizer/Abstractions/IAtomizerStorage.cs` | role-match |
| `src/Atomizer/Abstractions/IAtomizerServiceScope.cs` | interface | request-response | `src/Atomizer/Abstractions/IAtomizerStorage.cs` | role-match |
| `src/Atomizer/Abstractions/IAtomizerJobSerializer.cs` | interface | transform | `src/Atomizer/Abstractions/IAtomizerStorage.cs` | role-match |
| `src/Atomizer/Configuration/AtomizerOptions.cs` | config | request-response | `src/Atomizer/Configuration/QueueOptions.cs` | exact |
| `src/Atomizer/Configuration/QueueOptions.cs` | config | — | self (already documented) | exact |
| `src/Atomizer/Configuration/SchedulingOptions.cs` | config | — | `src/Atomizer/Configuration/QueueOptions.cs` | exact |
| `src/Atomizer/Configuration/AtomizerProcessingOptions.cs` | config | — | `src/Atomizer/Configuration/QueueOptions.cs` | role-match |
| `src/Atomizer/Configuration/JobStorageOptions.cs` | config | — | `src/Atomizer/Configuration/QueueOptions.cs` | role-match |
| `src/Atomizer/Configuration/ServiceCollectionExtensions.cs` | utility | request-response | `src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs` | role-match |
| `src/Atomizer/Models/AtomizerJob.cs` | model | CRUD | `src/Atomizer/Models/AtomizerSchedule.cs` | exact |
| `src/Atomizer/Models/AtomizerSchedule.cs` | model | CRUD | `src/Atomizer/Models/AtomizerJob.cs` | exact |
| `src/Atomizer/Models/AtomizerJobError.cs` | model | CRUD | `src/Atomizer/Models/AtomizerJob.cs` | role-match |
| `src/Atomizer/Models/Base/Model.cs` | model | — | `src/Atomizer/Models/Base/ValueObject.cs` | role-match |
| `src/Atomizer/Models/Base/ValueObject.cs` | model | — | `src/Atomizer/Models/Base/Model.cs` | role-match |
| `src/Atomizer/Models/ValueObjects/QueueKey.cs` | model | — | `src/Atomizer/Models/ValueObjects/JobKey.cs` | exact |
| `src/Atomizer/Models/ValueObjects/JobKey.cs` | model | — | `src/Atomizer/Models/ValueObjects/QueueKey.cs` | exact |
| `src/Atomizer/Models/ValueObjects/LeaseToken.cs` | model | — | `src/Atomizer/Models/ValueObjects/QueueKey.cs` | role-match |
| `src/Atomizer/Models/ValueObjects/RetryStrategy.cs` | model | — | `src/Atomizer/Models/ValueObjects/QueueKey.cs` | role-match |
| `src/Atomizer/Models/ValueObjects/Schedule.cs` | model | — | `src/Atomizer/Models/ValueObjects/QueueKey.cs` | role-match |
| `src/Atomizer/Exceptions/` (all 7 files) | utility | — | each other (identical structure) | exact |
| `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreJobStorageOptions.cs` | config | — | `src/Atomizer/Configuration/QueueOptions.cs` | role-match |
| `src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs` | utility | request-response | `src/Atomizer/Configuration/ServiceCollectionExtensions.cs` | role-match |
| `src/Atomizer.EntityFrameworkCore/Extensions/ModelBuilderExtensions.cs` | utility | — | `src/Atomizer/Configuration/ServiceCollectionExtensions.cs` | role-match |

---

## Pattern Assignments

### `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScope.cs` (utility, access-modifier change)

**Change:** One-line modifier swap; all logic stays identical.

**Before (line 10):**
```csharp
public class DatabaseTransactionLeasingScope : IDisposable, IAsyncDisposable
```

**After:**
```csharp
internal sealed class DatabaseTransactionLeasingScope : IDisposable, IAsyncDisposable
```

**Analog — `internal sealed class` pattern from the processing pipeline:**
Every file in `src/Atomizer/Processing/` already uses this declaration style. Representative example from `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs` (the peer leasing type):
- Use `internal sealed class` — no `public`, no non-sealed.
- No other changes to constructors, fields, or methods.

**Call-site safety:** `DatabaseTransactionLeasingScope.StartTransaction<TDbContext>` is called at
`EntityFrameworkCoreStorage.cs` lines 274 and 303. Because both files are in
`Atomizer.EntityFrameworkCore`, `internal` visibility is sufficient — no call-site changes required.

---

### `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs` — DELETE

**Action:** Delete file entirely.
**Justification:** Zero callers, no DI registration. Confirmed from reading the file — the only reference to `DatabaseTransactionLeasingScope.StartTransaction` is in `EntityFrameworkCoreStorage.cs` directly, not through this factory.

---

### `src/Atomizer/Atomizer.csproj` (config, `<GenerateDocumentationFile>` addition)

**Analog:** `src/Atomizer.EntityFrameworkCore/Atomizer.EntityFrameworkCore.csproj` (same structure)

**Existing first `<PropertyGroup>` (lines 2–12):**
```xml
<PropertyGroup>
    <TargetFrameworks>netstandard2.0;net8.0;net10.0</TargetFrameworks>
    <LangVersion>14</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningsNotAsErrors>NU1901,NU1902,NU1903,NU1904</WarningsNotAsErrors>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <AnalysisLevel>latest</AnalysisLevel>
    <EnablePackageValidation>true</EnablePackageValidation>
</PropertyGroup>
```

**Pattern:** Add `<GenerateDocumentationFile>true</GenerateDocumentationFile>` inside the first `<PropertyGroup>`, immediately after `<EnablePackageValidation>true</EnablePackageValidation>`. Do not create a new `<PropertyGroup>`.

**Result:**
```xml
<PropertyGroup>
    <TargetFrameworks>netstandard2.0;net8.0;net10.0</TargetFrameworks>
    <LangVersion>14</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningsNotAsErrors>NU1901,NU1902,NU1903,NU1904</WarningsNotAsErrors>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <AnalysisLevel>latest</AnalysisLevel>
    <EnablePackageValidation>true</EnablePackageValidation>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
</PropertyGroup>
```

---

### `src/Atomizer.EntityFrameworkCore/Atomizer.EntityFrameworkCore.csproj` (config, same change)

**Existing first `<PropertyGroup>` (lines 2–12):**
```xml
<PropertyGroup>
    <TargetFrameworks>net6.0;net8.0;net10.0</TargetFrameworks>
    <LangVersion>14</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningsNotAsErrors>NU1901,NU1902,NU1903,NU1904</WarningsNotAsErrors>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <AnalysisLevel>latest</AnalysisLevel>
    <EnablePackageValidation>true</EnablePackageValidation>
    <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>
```

**Pattern:** Same as `Atomizer.csproj` — add after `<ImplicitUsings>enable</ImplicitUsings>` (last existing entry in this block).

---

## XML Documentation Patterns

### Canonical XML Doc Style

`src/Atomizer/Abstractions/IAtomizerStorage.cs` is the gold standard — every public method has complete `<summary>`, `<param>`, and `<returns>` tags. Copy this style exactly.

**Full method documentation pattern (from `IAtomizerStorage.cs` lines 5–11):**
```csharp
/// <summary>
/// Inserts a new Atomizer job into the storage and returns its unique identifier.
/// </summary>
/// <param name="job">The Atomizer job to be inserted.</param>
/// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
/// <returns>The unique identifier of the inserted job.</returns>
Task<Guid> InsertAsync(AtomizerJob job, CancellationToken cancellationToken);
```

**Property documentation pattern (from `QueueOptions.cs` lines 6–13):**
```csharp
/// <summary>
/// Gets or sets the batch size for processing jobs.
/// <remarks>Default is 10, meaning that the queue will batch 10 jobs at a time.</remarks>
/// </summary>
public int BatchSize { get; set; } = 10;
```

**Rules extracted from existing docs:**
- `<summary>` uses plain prose sentences, ends with a period.
- `<param>` starts with a capital letter, ends with a period. Use active voice ("The job to be inserted." not "Job that gets inserted").
- `<returns>` for `Task<T>`: describe what T is (e.g. "The unique identifier of the inserted job."). For plain `Task`: "A task representing the asynchronous operation."
- `<remarks>` used for defaults and behavioral notes — placed *inside* `<summary>` block (see `QueueOptions.cs` lines 12–13) — not as a sibling element.
- No `<exception>` tags on existing docs — do not add them.
- `<see cref="..."/>` used when referencing another type inline (see `IAtomizerStorage.cs` line 79).

---

### Files with Missing or Partial XML Docs — per-file inventory

#### `src/Atomizer/Abstractions/IAtomizerClient.cs`

**Gap:** Interface methods `EnqueueAsync`, `ScheduleAsync`, `ScheduleRecurringAsync` and the interface itself have no XML docs (lines 4–26). `EnqueueOptions` and `RecurringOptions` properties already have docs (lines 31–83).

**Pattern to copy from `IAtomizerStorage.cs`:**
```csharp
/// <summary>
/// Enqueues a job with the specified payload for immediate processing.
/// </summary>
/// <typeparam name="TPayload">The type of the payload to enqueue.</typeparam>
/// <param name="payload">The payload to enqueue.</param>
/// <param name="configure">Optional delegate to configure enqueue options such as queue, idempotency key, and retry strategy.</param>
/// <param name="cancellation">Cancellation token to cancel the operation.</param>
/// <returns>The unique identifier of the enqueued job.</returns>
Task<Guid> EnqueueAsync<TPayload>(...);
```

Note: `IAtomizerClient` methods use `cancellation` (not `cancellationToken`) per the public API convention stated in CLAUDE.md.

#### `src/Atomizer/Abstractions/IAtomizerJob.cs`

**Gap:** Interface `IAtomizerJob<TPayload>` (line 4), method `HandleAsync` (line 6), class `JobContext` (line 9) — all undocumented. Properties have docs (lines 11–19).

**Pattern:**
```csharp
/// <summary>
/// Defines the handler for a job with the specified payload type.
/// </summary>
/// <typeparam name="TPayload">The type of the payload this handler processes.</typeparam>
public interface IAtomizerJob<in TPayload>
{
    /// <summary>
    /// Handles the job with the specified payload and context.
    /// </summary>
    /// <param name="payload">The deserialized job payload.</param>
    /// <param name="context">Context providing job metadata and a cancellation token.</param>
    /// <returns>A task representing the asynchronous handler execution.</returns>
    Task HandleAsync(TPayload payload, JobContext context);
}

/// <summary>
/// Provides metadata and cancellation support to a running job handler.
/// </summary>
public sealed class JobContext { ... }
```

#### `src/Atomizer/Abstractions/IAtomizerServiceScope.cs`

**Gap:** Both interfaces (`IAtomizerServiceScopeFactory`, `IAtomizerServiceScope`) and their members are undocumented.

**Pattern:**
```csharp
/// <summary>
/// Creates scoped service instances for job dispatch.
/// </summary>
public interface IAtomizerServiceScopeFactory
{
    /// <summary>
    /// Creates a new service scope for resolving scoped dependencies.
    /// </summary>
    /// <returns>A new <see cref="IAtomizerServiceScope"/> instance.</returns>
    IAtomizerServiceScope CreateScope();
}

/// <summary>
/// Represents a scoped service resolution context for a single job dispatch.
/// </summary>
public interface IAtomizerServiceScope : IDisposable
{
    /// <summary>
    /// Gets the storage instance for this scope.
    /// </summary>
    IAtomizerStorage Storage { get; }
}
```

#### `src/Atomizer/Abstractions/IAtomizerJobSerializer.cs`

**Gap:** Interface and both methods are undocumented.

**Pattern:**
```csharp
/// <summary>
/// Serializes and deserializes job payloads to and from their string representation.
/// </summary>
public interface IAtomizerJobSerializer
{
    /// <summary>
    /// Serializes the specified payload to a string.
    /// </summary>
    /// <typeparam name="TPayload">The type of the payload to serialize.</typeparam>
    /// <param name="payload">The payload to serialize.</param>
    /// <returns>The serialized string representation of the payload.</returns>
    string Serialize<TPayload>(TPayload payload);

    /// <summary>
    /// Deserializes the specified string to an object of the given type.
    /// </summary>
    /// <param name="payload">The serialized payload string.</param>
    /// <param name="payloadType">The target type to deserialize into.</param>
    /// <returns>The deserialized object, or <see langword="null"/> if deserialization produces no value.</returns>
    object? Deserialize(string payload, Type payloadType);
}
```

#### `src/Atomizer/Configuration/AtomizerOptions.cs`

**Gap:** Class itself, `JobStorageOptions` property (line 9), `AddQueue` (line 16), `AddHandlersFrom(Assembly[])` (line 41), `AddHandlersFrom<TMarker>` (line 69), `ConfigureScheduling` (line 71) — all undocumented.

**Pattern (copy from `IAtomizerStorage.cs` style):**
```csharp
/// <summary>
/// Configures the Atomizer background job system.
/// </summary>
public sealed class AtomizerOptions
{
    /// <summary>
    /// Gets or sets the storage backend options. Must be configured before the host starts.
    /// </summary>
    public JobStorageOptions? JobStorageOptions { get; set; }

    /// <summary>
    /// Adds a named queue with optional configuration.
    /// </summary>
    /// <param name="name">The unique name of the queue.</param>
    /// <param name="configure">Optional delegate to configure queue-specific options.</param>
    /// <returns>The current <see cref="AtomizerOptions"/> instance for chaining.</returns>
    public AtomizerOptions AddQueue(string name, Action<QueueOptions>? configure = null) { ... }

    /// <summary>
    /// Scans the specified assemblies for <see cref="IAtomizerJob{TPayload}"/> implementations and registers them as scoped services.
    /// </summary>
    /// <param name="assemblies">One or more assemblies to scan.</param>
    /// <returns>The current <see cref="AtomizerOptions"/> instance for chaining.</returns>
    public AtomizerOptions AddHandlersFrom(params Assembly[] assemblies) { ... }

    /// <summary>
    /// Scans the assembly containing <typeparamref name="TMarker"/> for <see cref="IAtomizerJob{TPayload}"/> implementations.
    /// </summary>
    /// <typeparam name="TMarker">A type whose assembly is scanned for job handlers.</typeparam>
    /// <returns>The current <see cref="AtomizerOptions"/> instance for chaining.</returns>
    public AtomizerOptions AddHandlersFrom<TMarker>() { ... }

    /// <summary>
    /// Configures the scheduling subsystem options.
    /// </summary>
    /// <param name="configure">Delegate to configure <see cref="SchedulingOptions"/>.</param>
    /// <returns>The current <see cref="AtomizerOptions"/> instance for chaining.</returns>
    public AtomizerOptions ConfigureScheduling(Action<SchedulingOptions> configure) { ... }
}
```

#### `src/Atomizer/Configuration/SchedulingOptions.cs`

**Gap:** Class itself (line 3) and constructor (line 29) are undocumented. Properties already have docs.

**Pattern:**
```csharp
/// <summary>
/// Configures the Atomizer scheduling subsystem.
/// </summary>
public class SchedulingOptions
{
    // ...existing documented properties...

    /// <summary>
    /// Initializes a new instance of <see cref="SchedulingOptions"/> with default values.
    /// </summary>
    /// <remarks>
    /// <see cref="ScheduleLeadTime"/> defaults to the larger of <see cref="StorageCheckInterval"/> and 1 second.
    /// </remarks>
    public SchedulingOptions() { ... }
}
```

#### `src/Atomizer/Configuration/AtomizerProcessingOptions.cs`

**Gap:** Class itself (line 3), both properties `StartupDelay` (line 5) and `GracefulShutdownTimeout` (line 6) are undocumented.

**Pattern (copy from `QueueOptions.cs`):**
```csharp
/// <summary>
/// Configures the Atomizer processing host services.
/// </summary>
public class AtomizerProcessingOptions
{
    /// <summary>
    /// Gets or sets the delay before the processing pipeline begins polling after host startup.
    /// <remarks>Defaults to no delay when <see langword="null"/>.</remarks>
    /// </summary>
    public TimeSpan? StartupDelay { get; set; }

    /// <summary>
    /// Gets or sets the maximum time to wait for in-flight jobs to complete during shutdown.
    /// <remarks>Default is 30 seconds.</remarks>
    /// </summary>
    public TimeSpan GracefulShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
```

#### `src/Atomizer/Configuration/JobStorageOptions.cs`

**Gap:** Class itself (line 3) and constructor (lines 8–14) are undocumented. Properties already have docs.

**Pattern:**
```csharp
/// <summary>
/// Holds the factory and lifetime for the <see cref="IAtomizerStorage"/> implementation.
/// </summary>
public class JobStorageOptions
{
    /// <summary>
    /// Initializes a new instance of <see cref="JobStorageOptions"/>.
    /// </summary>
    /// <param name="jobStorageFactory">Factory delegate that creates the storage instance from the service provider.</param>
    /// <param name="jobStorageLifetime">The DI lifetime for the storage service. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    public JobStorageOptions(
        Func<IServiceProvider, IAtomizerStorage> jobStorageFactory,
        ServiceLifetime jobStorageLifetime = ServiceLifetime.Singleton
    ) { ... }
}
```

#### `src/Atomizer/Configuration/ServiceCollectionExtensions.cs`

**Gap:** Class itself (line 11), `AddAtomizer` (line 13), `AddAtomizerProcessing` (line 53) are undocumented.

**Pattern:**
```csharp
/// <summary>
/// Extension methods for registering Atomizer services with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers core Atomizer services including storage, client, serializer, and job handlers.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">Optional delegate to configure <see cref="AtomizerOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAtomizer(
        this IServiceCollection services,
        Action<AtomizerOptions>? configure = null
    ) { ... }

    /// <summary>
    /// Registers the Atomizer background processing pipeline including queue and scheduler hosted services.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">Optional delegate to configure <see cref="AtomizerProcessingOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAtomizerProcessing(
        this IServiceCollection services,
        Action<AtomizerProcessingOptions>? configure = null
    ) { ... }
}
```

#### `src/Atomizer/Models/AtomizerJob.cs`

**Gap:** Class itself, enum `AtomizerJobStatus`, all properties, static `Create` factory, and all domain methods (`Lease`, `Release`, `Attempt`, `MarkAsCompleted`, `MarkAsFailed`, `Reschedule`) are undocumented.

**Pattern for domain aggregate class (copy from `IAtomizerStorage.cs` style):**
```csharp
/// <summary>
/// Represents a single enqueued or scheduled job in the Atomizer system.
/// </summary>
public class AtomizerJob : Model { ... }

/// <summary>
/// Gets or sets the queue this job belongs to.
/// </summary>
public QueueKey QueueKey { get; set; } = QueueKey.Default;

/// <summary>
/// Creates a new <see cref="AtomizerJob"/> in the <see cref="AtomizerJobStatus.Pending"/> state.
/// </summary>
/// <param name="queueKey">The queue to place the job in.</param>
/// <param name="payloadType">The CLR type of the serialized payload.</param>
/// <param name="payload">The serialized payload string.</param>
/// <param name="createdAt">The UTC time the job was created.</param>
/// <param name="scheduledAt">The earliest UTC time the job may be processed.</param>
/// <param name="retryStrategy">Optional retry strategy; defaults to <see cref="RetryStrategy.Default"/>.</param>
/// <param name="idempotencyKey">Optional key used to deduplicate identical jobs.</param>
/// <param name="scheduleJobKey">Optional key linking this job to a recurring schedule.</param>
/// <returns>A new <see cref="AtomizerJob"/> instance.</returns>
public static AtomizerJob Create(...) { ... }

/// <summary>
/// Transitions the job from <see cref="AtomizerJobStatus.Pending"/> to <see cref="AtomizerJobStatus.Processing"/>.
/// </summary>
/// <param name="leaseToken">The lease token identifying the owning worker.</param>
/// <param name="now">The current UTC time.</param>
/// <param name="visibilityTimeout">How long the job remains invisible to other workers.</param>
public void Lease(LeaseToken leaseToken, DateTimeOffset now, TimeSpan visibilityTimeout) { ... }
```

**`AtomizerJobStatus` enum pattern (copy from CLAUDE.md convention — enum values use XML docs):**
```csharp
/// <summary>
/// Represents the lifecycle state of an <see cref="AtomizerJob"/>.
/// </summary>
public enum AtomizerJobStatus
{
    /// <summary>The job is waiting to be picked up for processing.</summary>
    Pending = 1,
    /// <summary>The job has been leased and is actively being processed.</summary>
    Processing = 2,
    /// <summary>The job handler completed successfully.</summary>
    Completed = 3,
    /// <summary>The job exhausted all retry attempts and will not be retried.</summary>
    Failed = 4,
}
```

#### `src/Atomizer/Models/AtomizerSchedule.cs`

**Gap:** Class itself, enum `MisfirePolicy`, all properties, static `Create`, `GetOccurrences`, `UpdateNextOccurence`, `Disable` are undocumented.

**Pattern — identical style to `AtomizerJob.cs` above. Key method signatures:**
```csharp
/// <summary>
/// Represents a recurring schedule definition in the Atomizer system.
/// </summary>
public class AtomizerSchedule : Model { ... }

/// <summary>
/// Creates a new <see cref="AtomizerSchedule"/> with the initial next run time computed from the cron expression.
/// </summary>
/// <returns>A new <see cref="AtomizerSchedule"/> instance.</returns>
public static AtomizerSchedule Create(...) { ... }

/// <summary>
/// Returns the list of job occurrences that should be enqueued at or before <paramref name="now"/>,
/// applying the configured <see cref="MisfirePolicy"/>.
/// </summary>
/// <param name="now">The current UTC time.</param>
/// <returns>An ordered list of UTC occurrence timestamps to enqueue.</returns>
public List<DateTimeOffset> GetOccurrences(DateTimeOffset now) { ... }

/// <summary>
/// Advances <see cref="NextRunAt"/> to the next cron occurrence after <paramref name="horizon"/>.
/// </summary>
/// <param name="horizon">The UTC time from which to compute the next occurrence.</param>
/// <param name="now">The current UTC time used to set <see cref="UpdatedAt"/>.</param>
public void UpdateNextOccurence(DateTimeOffset horizon, DateTimeOffset now) { ... }

/// <summary>
/// Disables this schedule so it is no longer polled.
/// </summary>
/// <param name="now">The current UTC time used to set <see cref="UpdatedAt"/>.</param>
public void Disable(DateTimeOffset now) { ... }
```

**`MisfirePolicy` enum pattern:**
```csharp
/// <summary>
/// Controls how the scheduler handles a schedule whose <c>NextRunAt</c> was missed.
/// </summary>
public enum MisfirePolicy
{
    /// <summary>Skip the missed run and advance to the next scheduled occurrence.</summary>
    Ignore = 1,
    /// <summary>Enqueue one job immediately for the missed occurrence, then advance.</summary>
    ExecuteNow = 2,
    /// <summary>Enqueue one job per missed occurrence, up to <see cref="AtomizerSchedule.MaxCatchUp"/>.</summary>
    CatchUp = 3,
}
```

#### `src/Atomizer/Models/AtomizerJobError.cs`

**Gap:** Class itself, all properties, and static `Create` are undocumented.

**Pattern (same as `AtomizerJob.cs`):**
```csharp
/// <summary>
/// Records a single failed attempt for an <see cref="AtomizerJob"/>.
/// </summary>
public class AtomizerJobError : Model { ... }

/// <summary>
/// Creates a new <see cref="AtomizerJobError"/> capturing details from an exception.
/// </summary>
/// <param name="jobId">The identifier of the job that failed.</param>
/// <param name="createdAt">The UTC time the error was recorded.</param>
/// <param name="attempt">The attempt number on which the error occurred.</param>
/// <param name="exception">The exception that caused the failure, or <see langword="null"/> if unavailable.</param>
/// <param name="runtimeIdentity">The instance identifier of the worker that attempted the job.</param>
/// <returns>A new <see cref="AtomizerJobError"/> instance.</returns>
public static AtomizerJobError Create(...) { ... }
```

#### `src/Atomizer/Models/Base/Model.cs`

**Gap:** Abstract class itself and `Id` property are undocumented.

**Pattern:**
```csharp
/// <summary>
/// Base class for all Atomizer persistent entities, providing a unique identifier.
/// </summary>
public abstract class Model
{
    /// <summary>
    /// Gets or sets the unique identifier for this entity.
    /// </summary>
    public Guid Id { get; set; }
}
```

#### `src/Atomizer/Models/Base/ValueObject.cs`

**Gap:** Abstract class itself, `GetEqualityValues` (line 5), `Equals(object?)` (line 7), `Equals(ValueObject?)` (line 20), `GetHashCode` (line 25), `==` operator (line 34), `!=` operator (line 47) are undocumented.

**Pattern:**
```csharp
/// <summary>
/// Base class for Atomizer value objects that define equality by their component values.
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    /// <summary>
    /// Returns the sequence of values that define equality for this value object.
    /// </summary>
    /// <returns>An enumerable of the equality-defining component values.</returns>
    protected abstract IEnumerable<object> GetEqualityValues();
}
```

Operator and override docs follow the same brief style — one sentence, no params needed (operators can use `<inheritdoc/>`).

#### `src/Atomizer/Models/ValueObjects/QueueKey.cs`

**Gap:** Class itself (line 6), constructor (line 11), `Key` property (line 26), implicit operators (lines 28–29), `ToString` (line 31), `GetEqualityValues` (line 33) are undocumented. Static `Default` field (line 8) is undocumented.

**Pattern (copy style from `IAtomizerStorage.cs` and `QueueOptions.cs`):**
```csharp
/// <summary>
/// Identifies a job queue by name. Maximum length is 100 characters.
/// </summary>
public sealed class QueueKey : ValueObject
{
    /// <summary>The default queue key used when no queue is specified.</summary>
    public static readonly QueueKey Default = new QueueKey("default");

    /// <summary>
    /// Initializes a new <see cref="QueueKey"/> with the specified name.
    /// </summary>
    /// <param name="key">The queue name. Must be non-empty and at most 100 characters.</param>
    /// <exception cref="InvalidQueueKeyException">Thrown when <paramref name="key"/> is null, empty, or exceeds 100 characters.</exception>
    public QueueKey(string key) { ... }

    /// <summary>Gets the queue name.</summary>
    public string Key { get; }
}
```

**`JobKey.cs` follows the identical pattern:**
```csharp
/// <summary>
/// Identifies a recurring schedule by name. Maximum length is 255 characters.
/// </summary>
public sealed class JobKey : ValueObject { ... }
```

**`LeaseToken.cs` pattern:**
```csharp
/// <summary>
/// Identifies a batch of leased jobs held by a specific worker instance on a specific queue.
/// Format: <c>{InstanceId}:*:{QueueKey}:*:{LeaseId}</c>.
/// </summary>
public sealed class LeaseToken : ValueObject { ... }
```

**`RetryStrategy.cs` pattern — factory methods need full docs:**
```csharp
/// <summary>
/// Defines the retry behavior for a failed job, including the number of attempts and per-attempt delays.
/// </summary>
public sealed class RetryStrategy : ValueObject
{
    /// <summary>Gets the maximum number of attempts before the job is marked as failed.</summary>
    public int MaxAttempts { get; private set; } = 3;

    /// <summary>Gets the delay to apply before each retry attempt.</summary>
    public TimeSpan[] RetryIntervals { get; private set; } = [];

    /// <summary>Gets the default retry strategy: 3 attempts with 15-second fixed delays and ±20% jitter.</summary>
    public static RetryStrategy Default => Fixed(TimeSpan.FromSeconds(15), 3, jitter: true);

    /// <summary>Gets a strategy that makes a single attempt with no retries.</summary>
    public static RetryStrategy None => ...;

    /// <summary>
    /// Creates a strategy with a constant delay between attempts.
    /// </summary>
    /// <param name="delay">The fixed delay between attempts.</param>
    /// <param name="maxAttempts">The maximum number of attempts.</param>
    /// <param name="jitter">When <see langword="true"/>, applies ±20% random jitter to each interval.</param>
    /// <returns>A new <see cref="RetryStrategy"/> with constant delays.</returns>
    public static RetryStrategy Fixed(TimeSpan delay, int maxAttempts, bool jitter = false) { ... }

    /// <summary>
    /// Creates a strategy with explicit per-attempt delays.
    /// </summary>
    /// <param name="intervals">The ordered collection of delays, one per attempt.</param>
    /// <returns>A new <see cref="RetryStrategy"/> with the specified intervals.</returns>
    public static RetryStrategy Intervals(IEnumerable<TimeSpan> intervals) { ... }

    /// <summary>
    /// Creates a strategy with exponentially increasing delays between attempts.
    /// </summary>
    /// <param name="initialInterval">The delay before the first retry.</param>
    /// <param name="maxAttempts">The maximum number of attempts.</param>
    /// <param name="exponent">The growth factor per attempt. Must be greater than 1.0.</param>
    /// <param name="maxInterval">Optional upper bound on any single delay.</param>
    /// <param name="jitter">When <see langword="true"/>, applies ±20% random jitter to each interval.</param>
    /// <returns>A new <see cref="RetryStrategy"/> with exponential backoff.</returns>
    public static RetryStrategy Exponential(...) { ... }

    /// <summary>
    /// Returns <see langword="true"/> if another attempt should be made after the given attempt number.
    /// </summary>
    /// <param name="attempt">The zero-based attempt count already made.</param>
    /// <returns><see langword="true"/> when <paramref name="attempt"/> is less than <see cref="MaxAttempts"/>.</returns>
    public bool ShouldRetry(int attempt) { ... }

    /// <summary>
    /// Returns the delay to apply before the specified attempt.
    /// </summary>
    /// <param name="attempt">The one-based attempt number (1 = first retry).</param>
    /// <returns>The <see cref="TimeSpan"/> delay for the given attempt.</returns>
    public TimeSpan GetRetryInterval(int attempt) { ... }
}
```

**`Schedule.cs` pattern:**
```csharp
/// <summary>
/// Represents a 6-part cron schedule (seconds-level precision) used for recurring job definitions.
/// </summary>
public sealed class Schedule : ValueObject
{
    /// <summary>Gets the seconds field of the cron expression.</summary>
    public string Seconds { get; private set; } = "*";
    // ... same for Minutes, Hours, DayOfMonth, Month, DayOfWeek

    /// <summary>Gets a schedule that fires every second.</summary>
    public static Schedule EverySecond => ...;

    /// <summary>
    /// Creates a <see cref="Schedule"/> from a 5- or 6-part cron expression string.
    /// </summary>
    /// <param name="cronExpression">A standard 5-part or seconds-extended 6-part cron expression.</param>
    /// <returns>A new <see cref="Schedule"/> parsed from the expression.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="cronExpression"/> does not have 5 or 6 parts.</exception>
    public static Schedule Cron(string cronExpression) { ... }
}
```

#### Exceptions (`src/Atomizer/Exceptions/`)

All 7 exception types follow the identical pattern — only `<summary>` on the class and each constructor overload is needed. No properties or methods to document beyond constructors.

**Representative pattern for `ArgumentException`-derived types:**
```csharp
/// <summary>
/// Thrown when a <see cref="QueueKey"/> is constructed with an invalid value.
/// </summary>
public class InvalidQueueKeyException : ArgumentException
{
    /// <summary>
    /// Initializes a new instance with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidQueueKeyException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public InvalidQueueKeyException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>
    /// Initializes a new instance with the specified message and parameter name.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="paramName">The name of the parameter that caused the exception.</param>
    public InvalidQueueKeyException(string message, string paramName) : base(message, paramName) { }

    /// <summary>
    /// Initializes a new instance with the specified message, parameter name, and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="paramName">The name of the parameter that caused the exception.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public InvalidQueueKeyException(string message, string paramName, Exception innerException)
        : base(message, paramName, innerException) { }
}
```

**`Exception`-derived types** (`InvalidAtomizerConfigurationException`, `JobResolverException`, `PayloadSerializationException`) use the same pattern but omit the `paramName` overloads that do not exist for them. `JobResolverException` third constructor takes `Type payloadType` — document accordingly:
```csharp
/// <summary>
/// Initializes a new instance for the specified payload type, embedding the type name in the message.
/// </summary>
/// <param name="message">Additional detail about the resolution failure.</param>
/// <param name="payloadType">The payload type whose handler could not be resolved.</param>
public JobResolverException(string message, Type payloadType) : base(...) { }
```

`PayloadSerializationException` third constructor:
```csharp
/// <summary>
/// Initializes a new instance for the specified payload type, embedding the type name and direction in the message.
/// </summary>
/// <param name="message">Additional detail about the failure.</param>
/// <param name="payloadType">The payload type that failed to serialize or deserialize.</param>
/// <param name="deserialization">When <see langword="true"/>, the failure was during deserialization; otherwise serialization.</param>
public PayloadSerializationException(string message, Type payloadType, bool deserialization = false) : base(...) { }
```

#### `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreJobStorageOptions.cs`

**Gap:** Class itself (line 3) is undocumented. Properties already have docs.

**Pattern:**
```csharp
/// <summary>
/// Configures the Entity Framework Core storage backend for Atomizer.
/// </summary>
public class EntityFrameworkCoreJobStorageOptions { ... }
```

#### `src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs`

**Gap:** Class itself (line 9) and `UseEntityFrameworkCoreStorage<TDbContext>` (line 11) are undocumented.

**Pattern (copy from `ServiceCollectionExtensions.cs`):**
```csharp
/// <summary>
/// Extension methods for configuring the Entity Framework Core storage backend with Atomizer.
/// </summary>
public static class AtomizerOptionsExtensions
{
    /// <summary>
    /// Configures Atomizer to use Entity Framework Core as its storage backend.
    /// </summary>
    /// <typeparam name="TDbContext">The <see cref="DbContext"/> type that contains the Atomizer entity sets.</typeparam>
    /// <param name="options">The <see cref="AtomizerOptions"/> to configure.</param>
    /// <param name="configure">Optional delegate to configure <see cref="EntityFrameworkCoreJobStorageOptions"/>.</param>
    /// <returns>The <see cref="AtomizerOptions"/> instance for chaining.</returns>
    public static AtomizerOptions UseEntityFrameworkCoreStorage<TDbContext>(
        this AtomizerOptions options,
        Action<EntityFrameworkCoreJobStorageOptions>? configure = null
    ) { ... }
}
```

#### `src/Atomizer.EntityFrameworkCore/Extensions/ModelBuilderExtensions.cs`

**Gap:** Class itself (line 7) and `AddAtomizerEntities` (line 8) are undocumented.

**Pattern:**
```csharp
/// <summary>
/// Extension methods for configuring the EF Core model with Atomizer entity type configurations.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Applies Atomizer entity type configurations for jobs, job errors, and schedules.
    /// </summary>
    /// <param name="builder">The model builder to configure.</param>
    /// <param name="schema">The database schema to use for Atomizer tables. Defaults to <c>"Atomizer"</c>.</param>
    /// <returns>The <see cref="ModelBuilder"/> for chaining.</returns>
    public static ModelBuilder AddAtomizerEntities(this ModelBuilder builder, string? schema = "Atomizer") { ... }
}
```

---

## Shared Patterns

### `internal sealed class` Declaration
**Source:** All files in `src/Atomizer/Processing/` (e.g. `QueuePump.cs`, `QueuePoller.cs`, `JobWorker.cs`)
**Apply to:** `DatabaseTransactionLeasingScope.cs` only in this phase.
**Rule:** `internal sealed class TypeName : Interface1, Interface2` — both modifiers together, never one without the other for implementation types.

### XML Doc `<remarks>` placement
**Source:** `src/Atomizer/Configuration/QueueOptions.cs` (lines 12–13), `src/Atomizer/Abstractions/IAtomizerClient.cs` (lines 43–44)
**Apply to:** All option properties with defaults.
**Rule:** `<remarks>` is nested *inside* the `<summary>` block, not as a sibling element:
```csharp
/// <summary>
/// Gets or sets the foo.
/// <remarks>Default is X.</remarks>
/// </summary>
```

### XML Doc for `Task` vs `Task<T>` returns
**Source:** `src/Atomizer/Abstractions/IAtomizerStorage.cs` (lines 18–19 vs 10–11)
**Apply to:** All async methods across all documented files.
**Rule:**
- `Task<T>`: `<returns>` describes the T value directly — "The unique identifier of the inserted job."
- `Task` (no result): `<returns>A task representing the asynchronous operation.</returns>`
- `void`: no `<returns>` element.

### `cancellation` vs `cancellationToken` parameter name
**Source:** `src/Atomizer/Abstractions/IAtomizerClient.cs` (line 9) vs `src/Atomizer/Abstractions/IAtomizerStorage.cs` (line 11)
**Apply to:** All XML `<param>` for cancellation token parameters.
**Rule:** Public `IAtomizerClient` methods use `cancellation`; all internal/storage APIs use `cancellationToken`. Match the actual parameter name in each file.

---

## No Analog Found

All files in scope have analogs in the codebase. No files require falling back to RESEARCH.md patterns.

---

## Metadata

**Analog search scope:** `src/Atomizer/`, `src/Atomizer.EntityFrameworkCore/`
**Files scanned:** 27 source files read directly
**Pattern extraction date:** 2026-05-03
