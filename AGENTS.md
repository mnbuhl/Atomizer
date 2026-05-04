# AGENTS.md

Guidance for coding agents working in this repository.

## Commands

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run test projects
dotnet test tests/Atomizer.Tests/Atomizer.Tests.csproj
dotnet test tests/Atomizer.EntityFrameworkCore.Tests/Atomizer.EntityFrameworkCore.Tests.csproj

# Run a single test by name
dotnet test --filter "FullyQualifiedName~MethodName_WhenScenario_ShouldBehavior"

# Format
dotnet csharpier check .
dotnet csharpier format .
```

## Project Overview

Atomizer is a background job scheduling and queueing framework for ASP.NET Core, shipped as two packages:

- `src/Atomizer` — core library targeting `netstandard2.0;net8.0;net10.0`.
- `src/Atomizer.EntityFrameworkCore` — EF Core storage backend targeting `net6.0;net8.0;net10.0`.

Public entry point: `IAtomizerClient` supports `EnqueueAsync<TPayload>`, `ScheduleAsync<TPayload>`, and `ScheduleRecurringAsync<TPayload>`.

## Architecture Notes

- `IAtomizerStorage` owns job/schedule persistence, due-job acquisition, updates, and lease release.
- `IAtomizerLeasingScopeFactory` provides distributed locking for polling. EF Core uses a `ReadCommitted` transaction; in-memory uses per-queue `SemaphoreSlim`.
- Queue processing flow: `AtomizerQueueService` → `QueueCoordinator` → `QueuePump` → `QueuePoller` + `JobWorker` → `JobProcessor` → `DefaultJobDispatcher`.
- Scheduling flow: `AtomizerSchedulerService` → `SchedulePoller` → `ScheduleProcessor` → `AtomizerSchedule.GetOccurrences()`.
- Job state transitions must go through domain methods (`Lease`, `Attempt`, `MarkAsCompleted`, `MarkAsFailed`, `Reschedule`, `Release`), not direct status mutation.
- Cancellation uses two tokens: I/O shutdown first, execution cancellation only after the graceful shutdown timeout.
- Use `IAtomizerClock`; do not call `DateTimeOffset.UtcNow` directly in pipeline code.

## Storage Backends

- In-memory storage uses `ConcurrentDictionary<Guid, AtomizerJob>` plus queue indexes and retains only recent terminal jobs.
- EF Core storage uses `AtomizerJobEntity`, `AtomizerJobErrorEntity`, and `AtomizerScheduleEntity`; register with `modelBuilder.AddAtomizerEntities(schema: "atomizer")`.
- Supported EF providers: SQL Server, PostgreSQL, and MySQL. Unsupported providers require `AllowUnsafeProviderFallback = true`.
- EF due-job acquisition uses provider SQL via `ISqlDialect` for atomic locking / skip-locked behavior.

## Code Standards

- Use `System.Text.Json`; Newtonsoft is forbidden.
- Use `Microsoft.Extensions.DependencyInjection` and `Microsoft.Extensions.Logging` patterns already present.
- Run CSharpier before committing (`.csharpierrc`: 4 spaces, width 120).
- XML documentation is required for all public APIs.
- Core targets `netstandard2.0`; guard newer APIs such as `IAsyncDisposable` / `await using` with `#if NETCOREAPP3_0_OR_GREATER`.
- Public consumer APIs generally live in namespace `Atomizer`; internal namespaces mirror folders.
- One public type per file; filenames match type names.
- Prefer guard clauses and short methods. `CancellationToken` is the last parameter.
- Structured logging only; exception is the first argument when logging errors.
- Never let exceptions escape background loops; catch, log, and continue.

## Testing

- xUnit v3, NSubstitute, AwesomeAssertions, AutoFixture.
- Test names: `{Method}_When{Scenario}_Should{ExpectedBehavior}`.
- Test classes: `{Type}Tests` and mirror source structure.
- Shared helpers live in `tests/Atomizer.Tests.Utilities/`.
- EF integration tests use Testcontainers fixtures for PostgreSQL, SQL Server, and MySQL; SQLite does not need a container.

## Configuration Patterns

```csharp
services.AddAtomizer(options =>
{
    options.UseInMemoryStorage();
    options.AddQueue(QueueKey.Default, q => { });
    options.AddHandlersFrom<TMarker>();
    options.ConfigureScheduling(s => { });
});

services.AddAtomizerProcessing(options =>
{
    options.StartupDelay = TimeSpan.FromSeconds(5);
    options.GracefulShutdownTimeout = TimeSpan.FromSeconds(30);
});
```

A default queue is added automatically if none is configured. Handlers are registered as scoped and resolved in a fresh dispatch scope.

## Commit Style

Use Conventional Commits: `<type>[optional scope]: <description>` (max 72 chars). Common types: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, `revert`. Use `!` for breaking changes.
