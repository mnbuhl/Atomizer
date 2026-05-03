# Coding Conventions

**Analysis Date:** 2026-05-03

## Naming Patterns

**Files:**
- One public type per file; filename matches type name (e.g. `AtomizerJob.cs`, `QueuePump.cs`).
- Tests mirror source: `{Type}Tests.cs` (e.g. `src/Atomizer/Models/ValueObjects/RetryStrategy.cs` → `tests/Atomizer.Tests/Models/ValueObjects/RetryStrategyTests.cs`).
- Interfaces are co-located with their default implementation when they are internal (e.g. `IJobProcessor` declared at the top of `src/Atomizer/Processing/JobProcessor.cs`). Public abstractions live under `src/Atomizer/Abstractions/`.

**Types:**
- Classes: `PascalCase`, e.g. `AtomizerJob`, `QueuePump`, `DefaultJobDispatcher`.
- Interfaces: `IPascalCase`, e.g. `IAtomizerStorage`, `IAtomizerJob<TPayload>`, `IJobWorker`.
- Exceptions: `{Name}Exception` (e.g. `InvalidQueueKeyException`, `JobResolverException`). See `src/Atomizer/Exceptions/`.
- Enums: `PascalCase` with explicit numeric values when persisted (e.g. `AtomizerJobStatus { Pending = 1, Processing = 2, ... }` in `src/Atomizer/Models/AtomizerJob.cs:116-121`).

**Members:**
- Methods: `PascalCase`. Async methods end with `Async` (e.g. `InsertAsync`, `DispatchAsync`, `HandleAsync`).
- Properties: `PascalCase`.
- Parameters and local variables: `camelCase`.
- Private fields: `_camelCase` with leading underscore (e.g. `private readonly IAtomizerClock _clock;` in `src/Atomizer/Core/AtomizerClient.cs:10`).
- Static readonly singletons: `PascalCase` (e.g. `QueueKey.Default`, `RetryStrategy.Default`).

**Test methods:** `{Method}_When{Scenario}_Should{ExpectedBehavior}` — enforced across the codebase. Examples from `tests/Atomizer.Tests/Processing/JobProcessorTests.cs`:
- `ProcessAsync_WhenJobSucceeds_ShouldMarkCompletedAndUpdateStorageAndLog`
- `ProcessAsync_WhenOperationCanceled_ShouldLogWarning`
- `HandleFailureAsync_WhenShouldNotRetry_ShouldMarkFailedAndLogError`

## Code Style

**Formatting:**
- Tool: CSharpier. Run `dotnet csharpier .` before committing.
- Config: `.csharpierrc` at repo root — `printWidth: 120`, `useTabs: false`, `indentSize: 4`, `endOfLine: auto`.
- File-scoped namespaces everywhere (e.g. `namespace Atomizer.Processing;` in `src/Atomizer/Processing/JobProcessor.cs:5`).

**Compiler strictness (from `src/Atomizer/Atomizer.csproj` and `src/Atomizer.EntityFrameworkCore/Atomizer.EntityFrameworkCore.csproj`):**
- `<Nullable>enable</Nullable>` — nullable reference types required.
- `<ImplicitUsings>enable</ImplicitUsings>` for source projects.
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — warnings break the build (except NuGet advisory warnings `NU1901-NU1904`).
- `<AnalysisLevel>latest</AnalysisLevel>` — latest Roslyn analyzers.
- `<EnablePackageValidation>true</EnablePackageValidation>` for public-facing package stability.
- `<LangVersion>14</LangVersion>` for source, `12` for test projects.

**Target frameworks:**
- `src/Atomizer/Atomizer.csproj`: `netstandard2.0;net8.0;net10.0`.
- `src/Atomizer.EntityFrameworkCore/Atomizer.EntityFrameworkCore.csproj`: `net6.0;net8.0;net10.0`.
- Tests: `net6.0;net8.0;net10.0`.

## Multi-Targeting Patterns

Because the main library targets `netstandard2.0` alongside modern TFMs, APIs that depend on modern runtime features are guarded:

```csharp
// src/Atomizer/Abstractions/IAtomizerLeasingScope.cs
public interface IAtomizerLeasingScope : IDisposable
#if NETCOREAPP3_0_OR_GREATER
        , IAsyncDisposable
#endif
{
    bool Acquired { get; }
}
```

```csharp
// src/Atomizer/Processing/QueuePoller.cs:59
#if NETCOREAPP3_0_OR_GREATER
    await using var leasingScope = await leasingScopeFactory.CreateScopeAsync(...);
#else
    using var leasingScope = ...;
#endif
```

**Rule:** when adding any `IAsyncDisposable` / `await using` / `IAsyncEnumerable` usage in the main library, add a `#if NETCOREAPP3_0_OR_GREATER` (or more specific `#if NET6_0_OR_GREATER`) guard and provide a synchronous fallback for the `netstandard2.0` path.

## Import Organization

**Order (observed across files):**
1. `System.*` namespaces.
2. External packages (`Microsoft.*`, `Cronos`, `AwesomeAssertions`, etc.).
3. `Atomizer.*` project namespaces.

**Global usings:**
- Tests use `global using` aggregated in `tests/Atomizer.Tests/globals.cs`:
  ```csharp
  global using Atomizer.Tests.Utilities;
  global using AwesomeAssertions;
  global using NSubstitute;
  global using Xunit;
  ```
- Source projects rely on `<ImplicitUsings>enable</ImplicitUsings>` for common `System.*` usings.

**Assembly-level attributes:** Each project has an `AssemblyAttributes.cs` at the project root (e.g. `src/Atomizer/AssemblyAttributes.cs`, `tests/Atomizer.Tests/AssemblyAttributes.cs`) for `InternalsVisibleTo` and xUnit parallelism attributes.

## Access Modifiers

**Public surface is deliberately small:**
- Public: stable consumer-facing APIs under `src/Atomizer/Abstractions/`, value objects (`QueueKey`, `JobKey`, `RetryStrategy`, `Schedule`), `AtomizerJob`, `AtomizerSchedule`, configuration options (`AtomizerOptions`, `QueueOptions`, `SchedulingOptions`), `JobContext`, and exceptions.
- Internal: processing pipeline, schedulers, default implementations, and EF Core storage internals. Every file in `src/Atomizer/Processing/` uses `internal sealed class` (e.g. `QueuePump`, `JobWorker`, `JobProcessor`).
- `sealed` is applied to all internal implementations and to value objects (`public sealed class QueueKey` at `src/Atomizer/Models/ValueObjects/QueueKey.cs:6`). Only base classes (`Model`, `ValueObject`) are non-sealed.

## Domain Method Pattern

**Never mutate model state directly — call a domain method.** `AtomizerJob` (`src/Atomizer/Models/AtomizerJob.cs`) exposes:

```csharp
public void Lease(LeaseToken leaseToken, DateTimeOffset now, TimeSpan visibilityTimeout) { ... }
public void Release(DateTimeOffset now) { ... }
public void Attempt() { ... }
public void MarkAsCompleted(DateTimeOffset completedAt) { ... }
public void MarkAsFailed(DateTimeOffset failedAt) { ... }
public void Reschedule(DateTimeOffset nextVisibleAt, DateTimeOffset now) { ... }
```

Each method validates the current `Status` and throws `InvalidOperationException` when the transition is illegal:

```csharp
// src/Atomizer/Models/AtomizerJob.cs:54-63
public void Lease(LeaseToken leaseToken, DateTimeOffset now, TimeSpan visibilityTimeout)
{
    if (Status != AtomizerJobStatus.Pending)
    {
        throw new InvalidOperationException("Job must be in Pending status to lease.");
    }
    LeaseToken = leaseToken;
    VisibleAt = now.Add(visibilityTimeout);
    Status = AtomizerJobStatus.Processing;
    UpdatedAt = now;
}
```

Models derive from the tiny `Model` base class (`src/Atomizer/Models/Base/Model.cs`) which only supplies `Guid Id`. Construction uses a static `Create` factory (`AtomizerJob.Create`, `AtomizerJobError.Create`, `AtomizerSchedule.Create`) rather than a public constructor, to guarantee initial invariants.

## Value Object Pattern

All domain value objects inherit `ValueObject` (`src/Atomizer/Models/Base/ValueObject.cs`):
- Override `GetEqualityValues()` yielding the components that define equality.
- `Equals`, `GetHashCode`, and `==`/`!=` operators are implemented in the base class.
- Instances are immutable (`public sealed class`, no public setters, e.g. `QueueKey.Key` is init-only via constructor).

Example (`src/Atomizer/Models/ValueObjects/QueueKey.cs`):
```csharp
public sealed class QueueKey : ValueObject
{
    public static readonly QueueKey Default = new QueueKey("default");

    public QueueKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new InvalidQueueKeyException("Queue name cannot be null or empty.", nameof(key));
        if (key.Length > 100)
            throw new InvalidQueueKeyException("Queue name cannot exceed 100 characters.", nameof(key));
        Key = key;
    }

    public string Key { get; }

    public static implicit operator string(QueueKey queueKey) => queueKey.Key;
    public static implicit operator QueueKey(string name) => new QueueKey(name);
    public override string ToString() => Key;

    protected override IEnumerable<object> GetEqualityValues()
    {
        yield return Key;
    }
}
```

**Rules for new value objects:**
1. `public sealed class`, derive from `ValueObject`.
2. Validate every constructor argument; throw the matching `Invalid{Name}Exception` from `src/Atomizer/Exceptions/`.
3. Implement `GetEqualityValues()` returning every field that participates in equality.
4. Override `ToString()`.
5. Consider implicit conversions to `string` (or the primitive) for ergonomic API surfaces, as `QueueKey`/`JobKey` do.
6. Expose static readonly instances for canonical values (`Default`, `None`, predefined cron schedules).

## Error Handling

**Exception types (all in `src/Atomizer/Exceptions/`):**
- Derive from `ArgumentException` for invalid inputs with a `paramName` overload: `InvalidRetryStrategyException`, `InvalidQueueKeyException`, `InvalidJobKeyException`, `InvalidLeaseTokenException`.
- Derive from `Exception` for operational / configuration failures: `InvalidAtomizerConfigurationException`, `JobResolverException`, `PayloadSerializationException`.
- Each exception exposes four-ish constructors: `(message)`, `(message, innerException)`, `(message, paramName)` for `ArgumentException`-derived, and `(message, paramName, innerException)`.

**Rules:**
- Throw domain-specific exceptions at public API boundaries and inside value-object constructors. Never throw bare `Exception`.
- Use `InvalidOperationException` for illegal state transitions on aggregates (see `AtomizerJob.Lease`).
- In the processing pipeline, catch `OperationCanceledException` separately so shutdown is not treated as a failure:
  ```csharp
  // src/Atomizer/Processing/JobProcessor.cs:63-70
  catch (OperationCanceledException) when (ct.IsCancellationRequested)
  {
      _logger.LogWarning("Operation cancelled while processing job {JobId} on '{Queue}'", job.Id, job.QueueKey);
  }
  catch (Exception ex)
  {
      await HandleFailureAsync(job, ex, ct);
  }
  ```
- Never let exceptions escape background loops (poller, worker, pump). Catch → log → continue. The only exception is `HandleFailureAsync` itself, which also catches and logs so storage failures never crash the host.
- Unwrap `TargetInvocationException` before rethrowing so retry/failure logic sees the real exception (`DefaultJobDispatcher` in `src/Atomizer/Core/DefaultJobDispatcher.cs`).

## Cancellation Token Flow

The processing pipeline uses **two** linked cancellation tokens per queue (`src/Atomizer/Processing/QueuePump.cs:26-27`):

```csharp
private CancellationTokenSource _ioCts = new CancellationTokenSource();
private CancellationTokenSource _executionCts = new CancellationTokenSource();
```

- `_ioCts` — linked to the host cancellation token. Cancelled first during shutdown to stop the poller and signal workers to stop reading from the channel.
- `_executionCts` — cancelled only when the graceful shutdown deadline expires; interrupts running handlers mid-flight.

**Rule for pipeline code:**
- I/O / polling / storage calls take the `ioToken`.
- User handler invocation (`IAtomizerJob<T>.HandleAsync`) takes the `executionToken` via `JobContext.CancellationToken` (`src/Atomizer/Abstractions/IAtomizerJob.cs:18`).

**Rule for public async APIs:**
- Every async method has a `CancellationToken cancellationToken` parameter.
- Public `IAtomizerClient` methods use `CancellationToken cancellation = default` (see `src/Atomizer/Abstractions/IAtomizerClient.cs:9`); internal / storage APIs use `CancellationToken cancellationToken` with no default.
- Do not pass `CancellationToken.None` unless the call site is the shutdown cleanup path (e.g. `QueuePump.StopAsync` worker task, `src/Atomizer/Processing/QueuePump.cs:80-83`).

## Logging

**Framework:** `Microsoft.Extensions.Logging` (`ILogger<T>` / `ILogger`) — no Serilog, NLog, or `Console.WriteLine`.

**Patterns (from `src/Atomizer/Processing/JobProcessor.cs:40-61`):**
- **Structured logging only** — use placeholder names, never string interpolation:
  ```csharp
  _logger.LogDebug("Executing job {JobId} (attempt {Attempt}) on '{Queue}'",
      job.Id, job.Attempts, job.QueueKey);
  ```
- Conventional property names: `{JobId}`, `{Queue}`, `{QueueKey}`, `{Attempt}`, `{InstanceId}`, `{Ms}`, `{Delay}`.
- Log levels:
  - `LogDebug` — per-job lifecycle details, internal dispatch steps.
  - `LogInformation` — queue lifecycle (start/stop), job success, released leases.
  - `LogWarning` — transient failures, cancellation during processing, retry scheduling, shutdown timeout.
  - `LogError` — exhausted retries, failures inside the failure handler, unexpected exceptions. Include `Exception ex` as first argument when available.
  - `LogTrace` / `LogCritical` — rare; used only for noisy pump internals or fatal misconfiguration.
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

Every public member must carry XML docs. Observed conventions:
- `<summary>` is mandatory.
- `<param>` for every parameter, `<returns>` for non-void returns. See `src/Atomizer/Abstractions/IAtomizerStorage.cs:5-12` for the canonical example.
- `<remarks>` is used to state defaults and behavioral notes on option properties (see `src/Atomizer/Abstractions/IAtomizerClient.cs:42`).
- `<see cref="..."/>` is used in summaries to cross-reference related types (see test-class summaries such as `JobProcessorTests`).
- Internal types don't require XML docs, but public interfaces and their option bags must have them.

## Function Design

**Size / style:**
- Methods typically stay short (<80 lines). Longer methods (`QueuePump.StopAsync`, `src/Atomizer/Processing/QueuePump.cs:89-151`) are annotated with numbered step comments (`// 1) ... // 2) ...`).
- Early returns and guard clauses over nested `if/else`.
- `return new Foo { ... }` object-initializer style preferred over multi-statement construction (see `AtomizerJob.Create`).
- Collection initializers with modern syntax: `[]`, `new List<T>()`, `new[] { ... }`. The codebase mixes `[]` and `Array.Empty<T>()`; prefer `[]` in new code (C# 12+).

**Parameters:**
- Use `Action<TOptions>? configure = null` for optional fluent configuration (see `EnqueueAsync` in `src/Atomizer/Abstractions/IAtomizerClient.cs:6-10` and `AtomizerOptions.AddQueue`).
- `CancellationToken` is always the last parameter.
- Prefer `IReadOnlyList<T>` / `IEnumerable<T>` at boundaries for storage reads (`IAtomizerStorage.GetDueJobsAsync`).

## Module Design

**Namespaces:**
- Public consumer API types sit in the root `namespace Atomizer;` (e.g. `QueueKey`, `RetryStrategy`, `AtomizerJob`, `IAtomizerClient`, `JobContext`, `AtomizerOptions`). Several abstraction files explicitly annotate `// ReSharper disable once CheckNamespace` to keep them in the root namespace even though they live in a subfolder (`src/Atomizer/Abstractions/IAtomizerJob.cs:1`).
- Internal namespaces mirror folder layout: `Atomizer.Core`, `Atomizer.Processing`, `Atomizer.Scheduling`, `Atomizer.Storage`, `Atomizer.Models.Base`, `Atomizer.Exceptions`, `Atomizer.Abstractions` (for `IAtomizerStorage` / internal abstractions).
- EF Core project: `Atomizer.EntityFrameworkCore`, `Atomizer.EntityFrameworkCore.Storage`, `Atomizer.EntityFrameworkCore.Providers`, etc.

**Exports:**
- No barrel / `GlobalUsings.cs` files in source projects.
- New consumer-facing types should be placed in the root `Atomizer` namespace, using the `// ReSharper disable once CheckNamespace` comment when the file lives in a subfolder.

---

*Convention analysis: 2026-05-03*
