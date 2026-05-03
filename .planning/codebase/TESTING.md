# Testing Patterns

**Analysis Date:** 2026-05-03

## Test Framework

**Runner:**
- **xUnit v3** — `xunit.v3` 2.0.1, `xunit.runner.visualstudio` 3.0.2.
- `Microsoft.NET.Test.Sdk` 17.13.0.
- Test projects are `OutputType=Exe` and `IsTestProject=true` (see `tests/Atomizer.Tests/Atomizer.Tests.csproj:5-10`) — xUnit v3 ships its own entry point.
- Per-assembly config: `tests/Atomizer.Tests/xunit.runner.json`, `tests/Atomizer.EntityFrameworkCore.Tests/xunit.runner.json`. Both enable aggressive parallelism:
  ```json
  {
    "parallelizeTestCollections": true,
    "parallelizeAssembly": true,
    "parallelAlgorithm": "aggressive"
  }
  ```

**Assertion Library:**
- **AwesomeAssertions** 9.1.0 (FluentAssertions-compatible API). Usage: `value.Should().Be(...)`, `act.Should().Throw<T>().WithMessage("...")`, `collection.Should().ContainSingle()`.

**Mocking:**
- **NSubstitute** 5.3.0. `Substitute.For<T>()`, `Arg.Any<T>()`, `Arg.Is<T>(predicate)`, `.Returns(...)`, `.Received(n).Method(...)`.

**Test data:**
- **AutoFixture** 4.18.1 is referenced by `tests/Atomizer.Tests/Atomizer.Tests.csproj:29` but usage is opportunistic — most tests hand-build data through `AtomizerJob.Create(...)` or `FakeDataFactory` rather than `new Fixture().Create<T>()`.

**Coverage collector:**
- **coverlet.collector** 6.0.4 (referenced, outputs consumed by CI — no enforced threshold in-repo).

**Integration containers (EF Core tests only):**
- `Testcontainers.PostgreSql`, `Testcontainers.MsSql`, `Testcontainers.MySql` — all 4.6.0.
- SQLite tests run in-process via `Microsoft.EntityFrameworkCore.Sqlite` (no container).

**Target frameworks for tests:** `net6.0;net8.0;net10.0` — the same test is executed against all three TFMs.

**Run Commands:**
```bash
dotnet test                                                                # Run all test projects, all TFMs
dotnet test tests/Atomizer.Tests/Atomizer.Tests.csproj                     # Unit tests only
dotnet test tests/Atomizer.EntityFrameworkCore.Tests/Atomizer.EntityFrameworkCore.Tests.csproj  # EF Core integration
dotnet test --filter "FullyQualifiedName~MethodName_WhenScenario_ShouldBehavior"                # Single test
dotnet test -f net8.0                                                      # Single TFM
dotnet test --collect:"XPlat Code Coverage"                                # Produce coverage
```

Docker must be running for the PostgreSQL, SQL Server, and MySQL executors (`compose.yml` at repo root contains local dev containers; Testcontainers manages its own lifecycle per fixture).

## Test Project Organization

Three test projects mirror the two source packages:

```
tests/
├── Atomizer.Tests/                              # Unit tests for src/Atomizer
│   ├── globals.cs                               # Global usings (Xunit, NSubstitute, AwesomeAssertions, Atomizer.Tests.Utilities)
│   ├── xunit.runner.json                        # Aggressive parallelism
│   ├── Core/                                    # Mirrors src/Atomizer/Core
│   ├── Models/ValueObjects/                     # Mirrors src/Atomizer/Models/ValueObjects
│   ├── Processing/                              # Mirrors src/Atomizer/Processing
│   ├── Scheduling/                              # Mirrors src/Atomizer/Scheduling
│   └── Storage/                                 # InMemoryStorage + InMemoryLeasingScopeFactory tests
├── Atomizer.EntityFrameworkCore.Tests/          # Integration tests for EF Core storage
│   ├── Fixtures/                                # BaseDatabaseFixture + PostgreSql/SqlServer/MySql/Sqlite concretes
│   ├── TestSetup/                               # Per-provider DbContexts + DesignTimeDbContextFactory classes
│   │   ├── Postgres/, SqlServer/, MySql/, Sqlite/
│   └── Storage/                                 # EntityFrameworkCoreStorageTests (abstract) + executors per provider
└── Atomizer.Tests.Utilities/                    # Shared helpers (referenced by both test projects)
    ├── NonPublicSpy.cs                          # Reflection-based accessor for private members
    ├── TestableLogger.cs                        # Abstract ILogger<T> base suitable for NSubstitute
    ├── Stubs/FakeDataFactory.cs                 # Small factory helpers (e.g. LeaseToken)
    └── TestJobs/                                # Sample IAtomizerJob<T> implementations (WriteLineJob, LongRunningJob)
```

**Test class naming:** `{Type}Tests` — e.g. `JobProcessorTests`, `RetryStrategyTests`, `QueuePumpTests`, `InMemoryStorageTests`. Folder layout exactly mirrors the source project (`src/Atomizer/Processing/JobProcessor.cs` → `tests/Atomizer.Tests/Processing/JobProcessorTests.cs`).

**Test method naming:** `{Method}_When{Scenario}_Should{ExpectedBehavior}`. Enforced by GitHub Copilot instructions (`.github/copilot-instructions.md`) and observable in every test file. Examples:
- `ProcessAsync_WhenJobSucceeds_ShouldMarkCompletedAndUpdateStorageAndLog`
- `InsertAsync_WhenJobWithIdempotencyKeyExists_ShouldNotInsertDuplicateJob`
- `Fixed_WithNegativeDelay_ShouldThrow`
- `GetDueJobsAsync_WhenDueJobsExist_ShouldReturnDueJobs`

## Test Structure

**Constructor-based setup + class-level SUT fields.** xUnit treats each test method as a new class instance, so the constructor is the "Arrange common state" hook. From `tests/Atomizer.Tests/Processing/JobProcessorTests.cs:10-30`:

```csharp
public class JobProcessorTests
{
    private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();
    private readonly IAtomizerJobDispatcher _dispatcher = Substitute.For<IAtomizerJobDispatcher>();
    private readonly IAtomizerServiceScopeFactory _serviceScopeFactory = Substitute.For<IAtomizerServiceScopeFactory>();
    private readonly IAtomizerServiceScope _serviceScope = Substitute.For<IAtomizerServiceScope>();
    private readonly IAtomizerStorage _storage = Substitute.For<IAtomizerStorage>();
    private readonly TestableLogger _logger = Substitute.For<TestableLogger>();
    private readonly JobProcessor _sut;
    private readonly AtomizerJob _job;
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    public JobProcessorTests()
    {
        _clock.UtcNow.Returns(_now);
        _serviceScope.Storage.Returns(_storage);
        _serviceScopeFactory.CreateScope().Returns(_serviceScope);
        _sut = new JobProcessor(_clock, _dispatcher, _serviceScopeFactory, _logger);
        _job = AtomizerJob.Create(QueueKey.Default, typeof(string), "payload", _now, _now);
        _job.Status = AtomizerJobStatus.Processing;
    }

    [Fact]
    public async Task ProcessAsync_WhenJobSucceeds_ShouldMarkCompletedAndUpdateStorageAndLog()
    {
        // Arrange
        // Act
        await _sut.ProcessAsync(_job, CancellationToken.None);

        // Assert
        _job.Status.Should().Be(AtomizerJobStatus.Completed);
        _job.Attempts.Should().Be(1);
        _job.CompletedAt.Should().Be(_now);
        _job.Errors.Should().BeEmpty();

        await _storage.Received(1).UpdateJobsAsync(Arg.Any<AtomizerJob[]>(), Arg.Any<CancellationToken>());
        _logger.Received().LogInformation(Arg.Is<string>(s => s.StartsWith($"Job {_job.Id} succeeded in")));
    }
}
```

**Conventions:**
- Field name for system-under-test is `_sut`.
- Collaborators are stored in `private readonly` fields and named with an underscore prefix.
- Each test body is partitioned by `// Arrange`, `// Act`, `// Assert` comments (used in every test under `tests/Atomizer.Tests/**`). When arrange and act collapse, `// Arrange & Act` / `// Act & Assert` is used.
- Deterministic time: tests substitute `IAtomizerClock` and set a fixed `_now` (`_clock.UtcNow.Returns(_now);`). Never call `DateTimeOffset.UtcNow` inside a test body unless asserting ranges.
- Tests assert **both** state (mutations on `_sut`/domain objects) **and** collaboration (`_storage.Received(1)...`, `_logger.Received().LogInformation(...)`).

**No test base classes** for unit tests (constructors inline their own setup). Abstract bases are used only for the parameterized EF Core suite (see below).

## Facts, Theories & Fixtures

- **`[Fact]`** is the default. Theories are uncommon — a quick count shows the vast majority of ~120 tests are `[Fact]` based. When data-driving, prefer `[Theory]` + `[InlineData(...)]`.
- **`IClassFixture<T>`** / **`ICollectionFixture<T>`** are only used by the EF Core integration suite. There is no DI-based fixture plumbing in unit tests.
- `IAsyncLifetime` is used for async container setup (see `BaseDatabaseFixture<TDbContext>` below).

## Mocking with NSubstitute

**Substituting an interface:**
```csharp
private readonly IAtomizerStorage _storage = Substitute.For<IAtomizerStorage>();
```

**Return values:**
```csharp
_clock.UtcNow.Returns(_now);
_serviceScopeFactory.CreateScope().Returns(_serviceScope);
```

**Throwing from a mock:**
```csharp
_dispatcher.DispatchAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>())
           .Returns(_ => throw new InvalidOperationException("boom"));
```

**Verifying calls:**
```csharp
await _storage.Received(1).UpdateJobsAsync(Arg.Any<AtomizerJob[]>(), Arg.Any<CancellationToken>());

_logger.Received().LogWarning(
    Arg.Is<string>(s => s.StartsWith($"Job {_job.Id} failed (attempt {_job.Attempts}) on '{_job.QueueKey}', retrying after "))
);
```

**Mocking `ILogger<T>`:** the real `Microsoft.Extensions.Logging.ILogger` has generic `Log<TState>` which NSubstitute cannot easily assert against. The codebase solves this with the abstract `TestableLogger` / `TestableLogger<T>` (`tests/Atomizer.Tests.Utilities/TestableLogger.cs`), which flattens `Log<TState>(...)` into explicit `LogInformation(string)`, `LogError(Exception, string)`, `LogWarning(string)`, etc. Tests then substitute the abstract and assert on the friendly overloads:

```csharp
private readonly TestableLogger<InMemoryStorage> _logger = Substitute.For<TestableLogger<InMemoryStorage>>();
...
_logger.Received().LogError($"Job {_job.Id} exhausted retries and was marked as failed on '{_job.QueueKey}'");
```

**What to mock:**
- Every abstraction in `src/Atomizer/Abstractions/` (`IAtomizerStorage`, `IAtomizerJobSerializer`, `IAtomizerLeasingScope*`).
- Core cross-cutting services: `IAtomizerClock`, `IAtomizerJobDispatcher`, `IAtomizerServiceScopeFactory`, `IAtomizerServiceScope`.
- Loggers — always via `TestableLogger` / `TestableLogger<T>`, never the raw `ILogger<T>`.

**What NOT to mock:**
- Domain models and value objects (`AtomizerJob`, `AtomizerSchedule`, `QueueKey`, `RetryStrategy`). Construct real instances via factory methods (`AtomizerJob.Create(...)`).
- `InMemoryStorage` — used as-is (and is itself covered by `InMemoryStorageTests`).
- Simple POCOs like `QueueOptions`, `EnqueueOptions`, `RecurringOptions` — instantiate directly.

## Fixtures & Test Data

**`FakeDataFactory` (`tests/Atomizer.Tests.Utilities/Stubs/FakeDataFactory.cs`):**

```csharp
public static class FakeDataFactory
{
    public static LeaseToken LeaseToken() =>
        new LeaseToken($"instance1:*:{QueueKey.Default}:*:{Guid.NewGuid():N}");
}
```

Extend this class when a new value-object needs a canonical "good enough for tests" instance. Keep the factory small and side-effect free.

**Test jobs (`tests/Atomizer.Tests.Utilities/TestJobs/`):**

```csharp
// WriteLineJob.cs
public record WriteLineMessage(string Message);
public class WriteLineJob : IAtomizerJob<WriteLineMessage>
{
    public Task HandleAsync(WriteLineMessage payload, JobContext context)
    {
        Console.WriteLine(payload.Message);
        return Task.CompletedTask;
    }
}

// LongRunningJob.cs
public record LongRunningJobPayload(int DurationInSeconds);
public class LongRunningJob : IAtomizerJob<LongRunningJobPayload>
{
    public async Task HandleAsync(LongRunningJobPayload payload, JobContext context)
    {
        await Task.Delay(payload.DurationInSeconds * 1000, context.CancellationToken);
    }
}
```

These are shared between unit and integration tests; payloads are declared as `record` types.

**`NonPublicSpy` (`tests/Atomizer.Tests.Utilities/NonPublicSpy.cs`):**

Reflection-based accessor for private members used in tests that need to peek at internal state without changing visibility. Usage from `InMemoryStorageTests.cs:41-50`:

```csharp
var jobs = NonPublicSpy.GetFieldValue<InMemoryStorage, ConcurrentDictionary<Guid, AtomizerJob>>("_jobs", _sut);
jobs.Should().ContainKey(job.Id);

var queues = NonPublicSpy.GetFieldValue<InMemoryStorage, Dictionary<QueueKey, HashSet<Guid>>>("_queues", _sut);
queues[QueueKey.Default].Should().Contain(job.Id);
```

Supports `CreateFunc<TTarget, ...>(methodName)`, `CreateAction<TTarget, ...>(methodName)`, `GetFieldValue<TTarget, TValue>(name, instance)`, `GetPropertyValue<TTarget, TValue>(name, instance)` for both instance and static members, with caching via `ConcurrentDictionary`.

**Rule:** reach for `NonPublicSpy` only when the behavior truly cannot be observed through the public/internal API surface. Prefer asserting observable state or collaboration.

## EF Core Integration Tests (Testcontainers)

The EF Core test suite runs the **same** test logic against four providers. The pattern is:

1. **Abstract test class** containing all `[Fact]` methods, parameterized by `Func<TDbContext>` factory:
   ```csharp
   // tests/Atomizer.EntityFrameworkCore.Tests/Storage/EntityFrameworkCoreStorageTests.cs:15
   public abstract class EntityFrameworkCoreStorageTests : IAsyncLifetime
   {
       protected EntityFrameworkCoreStorageTests(
           Func<TestDbContext> contextFactory,
           EntityFrameworkCoreJobStorageOptions? options = null)
       { ... }

       [Fact]
       public async Task InsertAsync_WhenValidJob_ShouldInsertJob() { ... }
       // ...14 fact methods total
   }
   ```

2. **Per-provider fixture** derived from `BaseDatabaseFixture<TDbContext>` (`tests/Atomizer.EntityFrameworkCore.Tests/Fixtures/BaseDatabaseFixture.cs`):
   ```csharp
   public abstract class BaseDatabaseFixture<TDbContext> : IAsyncLifetime
       where TDbContext : TestDbContext
   {
       protected readonly IDatabaseContainer DatabaseContainer;
       public TDbContext DbContext { get; protected set; } = null!;

       public async ValueTask InitializeAsync()
       {
           RelationalProviderCache.ResetInstanceForTests();
           await DatabaseContainer.StartAsync();
           DbContext = ConfigureDbContext();
           await DbContext.Database.MigrateAsync();
       }

       protected abstract TDbContext ConfigureDbContext();
       public TDbContext CreateNewDbContext() => ConfigureDbContext();

       public async ValueTask DisposeAsync()
       {
           await DbContext.DisposeAsync();
           await DatabaseContainer.StopAsync();
           await DatabaseContainer.DisposeAsync();
       }
   }
   ```

3. **Concrete fixtures** spin up their Testcontainer:
   ```csharp
   // PostgreSqlDatabaseFixture.cs
   [CollectionDefinition(nameof(PostgreSqlDatabaseFixture))]
   public class PostgreSqlDatabaseFixture
       : BaseDatabaseFixture<PostgresDbContext>, ICollectionFixture<PostgreSqlDatabaseFixture>
   {
       public PostgreSqlDatabaseFixture()
           : base(new PostgreSqlBuilder()
               .WithDatabase("atomizer").WithUsername("postgres").WithPassword("secret")
               .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
               .Build()) { }

       protected override PostgresDbContext ConfigureDbContext()
       {
           var optionsBuilder = new DbContextOptionsBuilder<PostgresDbContext>();
           optionsBuilder.UseNpgsql(DatabaseContainer.GetConnectionString());
           return new PostgresDbContext(optionsBuilder.Options, "Atomizer");
       }
   }
   ```

   SQLite skips the container (`SqliteDatabaseFixture.cs`) and uses `Data Source=atomizer_db_{Guid:N}.db`.

4. **Executor class** wires the abstract suite to a provider using a primary constructor:
   ```csharp
   // EntityFrameworkCoreStorageTests.cs:641-660
   [Collection(nameof(PostgreSqlDatabaseFixture))]
   public class PostgreSqlStorageTestsExecutor(PostgreSqlDatabaseFixture fixture)
       : EntityFrameworkCoreStorageTests(fixture.CreateNewDbContext);

   [Collection(nameof(SqliteDatabaseFixture))]
   public class SqliteStorageTestsExecutor(SqliteDatabaseFixture fixture)
       : EntityFrameworkCoreStorageTests(
           fixture.CreateNewDbContext,
           new EntityFrameworkCoreJobStorageOptions { AllowUnsafeProviderFallback = true });
   ```

**Rules for extending:**
- Adding a new storage test case → add a single `[Fact]` to the abstract `EntityFrameworkCoreStorageTests`; it automatically runs against every provider.
- Adding a new provider → create a `XDbContext` in `TestSetup/X/`, a `XDesignTimeDbContextFactory`, a `XDatabaseFixture : BaseDatabaseFixture<XDbContext>, ICollectionFixture<XDatabaseFixture>`, and an executor class with `[Collection(nameof(XDatabaseFixture))]`.
- Each fresh `_dbContextFactory()` call produces a new `DbContext` to avoid change-tracker cross-contamination between arrange/act/assert phases. Call `dbContext.ChangeTracker.Clear()` after seeding and before the act phase when sharing one context (see `EntityFrameworkCoreStorageTests.cs:126`).
- `TestContext.Current.CancellationToken` (an xUnit v3 API) is threaded through `ToListAsync(...)` so test timeouts cancel EF queries cleanly.
- Design-time context factories (`TestSetup/X/XDesignTimeDbContextFactory.cs`) exist so `dotnet ef migrations add` can generate migrations against each provider; see `migrations.ps1` / `test-migrations.ps1` at the repo root for the workflow.

## Coverage

- **No enforced thresholds** in-repo (no `coverlet.runsettings`, no CI gate visible). `coverlet.collector` is referenced so `dotnet test --collect:"XPlat Code Coverage"` emits Cobertura reports. Treat coverage as advisory.
- Observed coverage focus: every public API on `AtomizerJob`, `RetryStrategy`, `Schedule`, `QueueKey`, `JobKey`, `LeaseToken`, `IAtomizerStorage` (both backends), and every class in `Processing/` and `Scheduling/` has dedicated `*Tests.cs`.

## Test Types

**Unit Tests (`tests/Atomizer.Tests/`):**
- Scope: one type / one method per test. Pure in-process; uses NSubstitute for all I/O.
- Fast — no containers, no filesystem, deterministic clock.
- ~100+ tests spread across Core, Models, Processing, Scheduling, Storage (in-memory).

**Integration Tests (`tests/Atomizer.EntityFrameworkCore.Tests/`):**
- Scope: `IAtomizerStorage` contract + `DatabaseTransactionLeasingScope` behavior against a real database.
- Testcontainers manage PostgreSQL / SQL Server / MySQL lifecycle per-collection; SQLite runs in-process.
- Use `IAsyncLifetime` for async container boot / migration.
- Each method uses `await using var dbContext = _dbContextFactory();` to isolate the DbContext scope.

**E2E Tests:** not present. End-to-end workflows are exercised via the sample apps under `samples/Atomizer.Example` and `samples/Atomizer.EFCore.Example` (not automated).

## Common Patterns

**Async testing:**
```csharp
[Fact]
public async Task InsertAsync_WhenValidJob_ShouldInsertJob()
{
    // Arrange
    var now = _clock.UtcNow;
    var job = AtomizerJob.Create(QueueKey.Default, typeof(WriteLineMessage), """{ "message": "Hello" }""", now, now);

    // Act
    await using var dbContext = _dbContextFactory();
    var storage = _storageFactory(dbContext);
    var jobId = await storage.InsertAsync(job, CancellationToken.None);

    // Assert
    jobId.Should().Be(job.Id);
}
```

**Error / exception testing:**
```csharp
[Fact]
public void Fixed_WithNegativeDelay_ShouldThrow()
{
    // Arrange & Act
    Action act = () => RetryStrategy.Fixed(TimeSpan.FromSeconds(-1), 2);

    // Assert
    act.Should().Throw<InvalidRetryStrategyException>().WithMessage("*Delay cannot be negative*");
}
```

Patterns:
- Synchronous throw → `Action act = () => ...; act.Should().Throw<T>().WithMessage("*substr*");`.
- Async throw → `Func<Task> act = async () => await ...; await act.Should().ThrowAsync<T>();`.
- Use glob `*pattern*` in `.WithMessage(...)` rather than exact strings so tests survive minor message wording changes.

**Cancellation testing:**
```csharp
var cts = new CancellationTokenSource();
cts.Cancel();
_dispatcher.DispatchAsync(Arg.Any<AtomizerJob>(), Arg.Any<CancellationToken>())
           .Returns(_ => throw new OperationCanceledException());

await _sut.ProcessAsync(_job, cts.Token);

_logger.Received().LogWarning($"Operation cancelled while processing job {_job.Id} on '{_job.QueueKey}'");
```

**State + collaboration verification:** every behavior test typically asserts (1) mutations on the domain aggregate, (2) calls on collaborators (`_storage.Received(1).UpdateJobsAsync(...)`), and (3) log output (`_logger.Received().LogInformation(...)`).

**Parallelism notes:**
- Assembly and test collections run in parallel (`"parallelAlgorithm": "aggressive"`). Unit tests must therefore be independent of process-wide static state.
- EF Core executors use `[Collection(nameof(XDatabaseFixture))]` to serialize tests that share a single Testcontainer instance.
- `RelationalProviderCache.ResetInstanceForTests()` is called in `BaseDatabaseFixture.InitializeAsync()` because the provider detection uses a process-wide cache — resetting it per fixture avoids cross-provider pollution.

---

*Testing analysis: 2026-05-03*
