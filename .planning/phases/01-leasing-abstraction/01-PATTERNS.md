# Phase 1: Leasing Abstraction - Pattern Map

**Mapped:** 2026-05-03
**Files analyzed:** 15 (4 deleted, 11 modified)
**Analogs found:** 15 / 15

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `src/Atomizer/Abstractions/IAtomizerStorage.cs` | abstraction (interface) | request-response | Self (existing interface) | exact — add two members |
| `src/Atomizer/Abstractions/IAtomizerServiceScope.cs` | abstraction (interface) | request-response | Self (existing interface) | exact — remove one property |
| `src/Atomizer/Core/ServiceProviderServiceScope.cs` | service (scope impl) | request-response | Self (existing class) | exact — remove one property |
| `src/Atomizer/Configuration/AtomizerOptions.cs` | config | — | Self (existing class) | exact — remove one field |
| `src/Atomizer/Configuration/ServiceCollectionExtensions.cs` | config (DI wiring) | — | Self (existing class) | exact — remove one registration block |
| `src/Atomizer/Configuration/AtomizerOptionsExtensions.cs` | config (extension) | — | Self (existing class) | exact — remove one assignment |
| `src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs` | config (extension) | — | Self (existing class) | exact — remove one assignment |
| `src/Atomizer/Processing/QueuePoller.cs` | service (poller) | event-driven | Self (existing class) | exact — rewrite leasing block |
| `src/Atomizer/Scheduling/SchedulePoller.cs` | service (poller) | event-driven | Self (existing class) | exact — rewrite leasing block |
| `src/Atomizer/Storage/InMemoryStorage.cs` | service (storage impl) | CRUD | Self (existing class) | exact — add two stub methods |
| `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` | service (storage impl) | CRUD | Self (existing class) | exact — add two stub methods |
| `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs` | service (factory) | request-response | Self (existing class) | exact — remove NoopLeasingScopeFactory reference |
| `src/Atomizer/Abstractions/IAtomizerLeasingScope.cs` | abstraction | — | n/a (DELETED) | — |
| `src/Atomizer/Abstractions/IAtomizerLeasingScopeFactory.cs` | abstraction | — | n/a (DELETED) | — |
| `src/Atomizer/Core/NoopLeasingScopeFactory.cs` | service | — | n/a (DELETED) | — |
| `src/Atomizer/Configuration/LeasingScopeOptions.cs` | config | — | n/a (DELETED) | — |
| `tests/Atomizer.Tests/Core/NoopLeasingScopeFactoryTests.cs` | test | — | n/a (DELETED) | — |
| `tests/Atomizer.Tests/Processing/QueuePollerTests.cs` | test | — | Self (existing class) | exact — remove IAtomizerLeasingScopeFactory mocks |
| `tests/Atomizer.Tests/Scheduling/SchedulePollerTests.cs` | test | — | Self (existing class) | exact — remove IAtomizerLeasingScopeFactory mocks |

---

## Pattern Assignments

### `src/Atomizer/Abstractions/IAtomizerStorage.cs` (abstraction, add two members)

**Analog:** Self — current content of `src/Atomizer/Abstractions/IAtomizerStorage.cs`

**Current interface namespace/header** (lines 1–3):
```csharp
namespace Atomizer.Abstractions;

public interface IAtomizerStorage
{
```

**XML doc style to match — existing member example** (lines 5–11):
```csharp
    /// <summary>
    /// Inserts a new Atomizer job into the storage and returns its unique identifier.
    /// </summary>
    /// <param name="job">The Atomizer job to be inserted.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The unique identifier of the inserted job.</returns>
    Task<Guid> InsertAsync(AtomizerJob job, CancellationToken cancellationToken);
```

**Two new members to append (from CONTEXT.md + RESEARCH.md — verbatim agreed signatures):**
```csharp
    /// <summary>
    /// Executes the specified callback within an exclusive lease for the given queue.
    /// The backend acquires its lock or transaction before invoking the callback and
    /// releases or commits it after the callback completes. If the callback throws,
    /// the lease is rolled back or released and the exception is rethrown.
    /// </summary>
    /// <typeparam name="TResult">The type of value returned by the callback.</typeparam>
    /// <param name="queue">The queue key identifying the lease boundary.</param>
    /// <param name="callback">
    /// The work to execute inside the lease. Receives a <see cref="CancellationToken"/>
    /// that is cancelled if the lease expires or the host shuts down.
    /// </param>
    /// <param name="cancellationToken">Cancellation token to cancel the lease acquisition.</param>
    /// <returns>The value returned by <paramref name="callback"/>.</returns>
    Task<TResult> ExecuteInLeaseAsync<TResult>(
        QueueKey queue,
        Func<CancellationToken, Task<TResult>> callback,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Executes the specified callback within an exclusive lease for the given queue.
    /// The backend acquires its lock or transaction before invoking the callback and
    /// releases or commits it after the callback completes. If the callback throws,
    /// the lease is rolled back or released and the exception is rethrown.
    /// </summary>
    /// <param name="queue">The queue key identifying the lease boundary.</param>
    /// <param name="callback">
    /// The work to execute inside the lease. Receives a <see cref="CancellationToken"/>
    /// that is cancelled if the lease expires or the host shuts down.
    /// </param>
    /// <param name="cancellationToken">Cancellation token to cancel the lease acquisition.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ExecuteInLeaseAsync(
        QueueKey queue,
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken
    );
```

**No `#if` guards needed.** Generic methods on interfaces are `netstandard2.0`-compatible. The `#if NETCOREAPP3_0_OR_GREATER` pattern only applies to `IAsyncDisposable`, not `Task`-returning methods.

---

### `src/Atomizer/Abstractions/IAtomizerServiceScope.cs` (abstraction, remove one property)

**Analog:** Self — current content of `src/Atomizer/Abstractions/IAtomizerServiceScope.cs`

**Current file** (lines 1–12):
```csharp
namespace Atomizer.Abstractions;

public interface IAtomizerServiceScopeFactory
{
    IAtomizerServiceScope CreateScope();
}

public interface IAtomizerServiceScope : IDisposable
{
    IAtomizerStorage Storage { get; }
    IAtomizerLeasingScopeFactory LeasingScopeFactory { get; }  // REMOVE this line
}
```

**Target state after edit — remove line 11:**
```csharp
namespace Atomizer.Abstractions;

public interface IAtomizerServiceScopeFactory
{
    IAtomizerServiceScope CreateScope();
}

public interface IAtomizerServiceScope : IDisposable
{
    IAtomizerStorage Storage { get; }
}
```

---

### `src/Atomizer/Core/ServiceProviderServiceScope.cs` (service, remove one property)

**Analog:** Self — current content of `src/Atomizer/Core/ServiceProviderServiceScope.cs`

**Current `ServiceProviderServiceScope` class** (lines 15–29):
```csharp
internal sealed class ServiceProviderServiceScope : IAtomizerServiceScope
{
    private readonly IServiceScope _scope;
    public IAtomizerStorage Storage { get; }
    public IAtomizerLeasingScopeFactory LeasingScopeFactory { get; }  // REMOVE

    public ServiceProviderServiceScope(IServiceScope scope)
    {
        _scope = scope;
        Storage = scope.ServiceProvider.GetRequiredService<IAtomizerStorage>();
        LeasingScopeFactory = scope.ServiceProvider.GetRequiredService<IAtomizerLeasingScopeFactory>();  // REMOVE
    }

    public void Dispose() => _scope.Dispose();
}
```

**Target state — remove the `LeasingScopeFactory` property and its constructor line:**
```csharp
internal sealed class ServiceProviderServiceScope : IAtomizerServiceScope
{
    private readonly IServiceScope _scope;
    public IAtomizerStorage Storage { get; }

    public ServiceProviderServiceScope(IServiceScope scope)
    {
        _scope = scope;
        Storage = scope.ServiceProvider.GetRequiredService<IAtomizerStorage>();
    }

    public void Dispose() => _scope.Dispose();
}
```

The `ServiceProviderServiceScopeFactory` class (lines 1–13) is unchanged.

---

### `src/Atomizer/Configuration/AtomizerOptions.cs` (config, remove one field)

**Analog:** Self — current content of `src/Atomizer/Configuration/AtomizerOptions.cs`

**Field to remove** (lines 11–12):
```csharp
    public LeasingScopeOptions LeasingScopeOptions { get; set; } =
        new LeasingScopeOptions(_ => new NoopLeasingScopeFactory());
```

Remove those two lines entirely. No replacement — the concept is gone. All other fields and methods remain unchanged.

---

### `src/Atomizer/Configuration/ServiceCollectionExtensions.cs` (config, remove DI registration block)

**Analog:** Self — current content of `src/Atomizer/Configuration/ServiceCollectionExtensions.cs`

**Block to remove** (lines 50–57):
```csharp
        services.Add(
            ServiceDescriptor.Describe(
                typeof(IAtomizerLeasingScopeFactory),
                options.LeasingScopeOptions.LockProviderFactory,
                options.LeasingScopeOptions.LockProviderLifetime
            )
        );
```

Remove those 7 lines entirely. Also remove the `using Atomizer.Abstractions;` import line (line 2) only if it becomes unused after this removal — verify first. The `IAtomizerStorage` registration block (lines 42–48) is the pattern to preserve.

---

### `src/Atomizer/Configuration/AtomizerOptionsExtensions.cs` (config extension, remove one assignment)

**Analog:** Self — current content of `src/Atomizer/Configuration/AtomizerOptionsExtensions.cs`

**Lines to remove** (lines 22–25):
```csharp
        options.LeasingScopeOptions = new LeasingScopeOptions(sp => new InMemoryLeasingScopeFactory(
            sp.GetRequiredService<IAtomizerClock>(),
            sp.GetRequiredService<ILogger<InMemoryLeasingScopeFactory>>()
        ));
```

Remove those 4 lines. The `InMemoryLeasingScopeFactory` import and the `JobStorageOptions` assignment block (lines 17–21) survive unchanged. `InMemoryLeasingScopeFactory` itself survives until Phase 5 — only the `options.LeasingScopeOptions` assignment is removed.

---

### `src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs` (config extension, remove one assignment)

**Analog:** Self — current content of `src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs`

**Lines to remove** (lines 29–35):
```csharp
        options.LeasingScopeOptions = new LeasingScopeOptions(
            sp => new DatabaseTransactionLeasingScopeFactory<TDbContext>(
                sp.GetRequiredService<TDbContext>(),
                sp.GetRequiredService<ILogger<DatabaseTransactionLeasingScopeFactory<TDbContext>>>()
            ),
            ServiceLifetime.Scoped
        );
```

Remove those 7 lines. The `JobStorageOptions` assignment block (lines 20–28) survives unchanged. `DatabaseTransactionLeasingScopeFactory` itself survives until Phase 5 — only the `options.LeasingScopeOptions` assignment is removed.

---

### `src/Atomizer/Processing/QueuePoller.cs` (service/poller, replace leasing block)

**Analog:** Self — current content of `src/Atomizer/Processing/QueuePoller.cs`

**Current leasing block to REPLACE** (lines 54–98) — the entire `using var scope ... if (leasingScope.Acquired)` block:
```csharp
                    using var scope = _serviceScopeFactory.CreateScope();

                    _lastStorageCheck = now;
                    var leasingScopeFactory = scope.LeasingScopeFactory;

#if NETCOREAPP3_0_OR_GREATER
                    await using var leasingScope = await leasingScopeFactory.CreateScopeAsync(
                        queue.QueueKey,
                        queue.VisibilityTimeout,
                        ct
                    );
#else
                    using var leasingScope = await leasingScopeFactory.CreateScopeAsync(
                        queue.QueueKey,
                        queue.VisibilityTimeout,
                        ct
                    );
#endif
                    var storage = scope.Storage;

                    if (leasingScope.Acquired)
                    {
                        var jobs = await storage.GetDueJobsAsync(queue.QueueKey, now, queue.BatchSize, ct);

                        if (jobs.Count > 0)
                        {
                            _logger.LogDebug("Queue '{Queue}' leasing {JobCount} job(s)", queue.QueueKey, jobs.Count);

                            foreach (var job in jobs)
                            {
                                job.Lease(leaseToken, now, queue.VisibilityTimeout);
                                leasedJobs.Add(job);
                            }

                            await storage.UpdateJobsAsync(leasedJobs, ct);
                        }
                        else
                        {
                            _logger.LogDebug("Queue '{Queue}' found no jobs to lease", queue.QueueKey);
                        }
                    }
                    else
                    {
                        _logger.LogDebug("Failed to acquire processing scope for queue '{Queue}'", queue.QueueKey);
                    }
```

**Replacement — new `ExecuteInLeaseAsync` call site:**
```csharp
                    using var scope = _serviceScopeFactory.CreateScope();
                    _lastStorageCheck = now;
                    var storage = scope.Storage;

                    leasedJobs = await storage.ExecuteInLeaseAsync(queue.QueueKey, async innerCt =>
                    {
                        var jobs = await storage.GetDueJobsAsync(queue.QueueKey, now, queue.BatchSize, innerCt);
                        var acquired = new List<AtomizerJob>();

                        if (jobs.Count > 0)
                        {
                            _logger.LogDebug("Queue '{Queue}' leasing {JobCount} job(s)", queue.QueueKey, jobs.Count);

                            foreach (var job in jobs)
                            {
                                job.Lease(leaseToken, now, queue.VisibilityTimeout);
                                acquired.Add(job);
                            }

                            await storage.UpdateJobsAsync(acquired, innerCt);
                        }
                        else
                        {
                            _logger.LogDebug("Queue '{Queue}' found no jobs to lease", queue.QueueKey);
                        }

                        return acquired;
                    }, ct);
```

**Critical structural notes:**
- `var leasedJobs = new List<AtomizerJob>();` (line 45) moves from being initialized before the `try` to being assigned the return value of `ExecuteInLeaseAsync`. Change the declaration on line 45 to `List<AtomizerJob> leasedJobs;` (no initializer) and assign inside the `if` block as shown.
- The channel write loop (lines 111–135) remains OUTSIDE the callback, unchanged — it iterates `leasedJobs` after `ExecuteInLeaseAsync` returns.
- The outer `if (now - _lastStorageCheck >= storageCheckInterval && itemsInChannel < queue.DegreeOfParallelism)` check (line 52) remains intact — `ExecuteInLeaseAsync` replaces only the inner leasing scope acquisition.
- Remove the `#if NETCOREAPP3_0_OR_GREATER` / `#else` / `#endif` block entirely — it was only needed for `await using` disposal of `IAtomizerLeasingScope`.
- The `leasedJobs` variable must be declared before the `try` block so the channel write loop (which is outside the `try`) can read it. Initialize as `List<AtomizerJob> leasedJobs = [];`.

---

### `src/Atomizer/Scheduling/SchedulePoller.cs` (service/poller, replace leasing block)

**Analog:** Self — current content of `src/Atomizer/Scheduling/SchedulePoller.cs`

**Current leasing block to REPLACE** (lines 51–95) — the `using var scope ... if (leasingScope.Acquired)` block:
```csharp
                    using var scope = _serviceScopeFactory.CreateScope();
                    var leasingScopeFactory = scope.LeasingScopeFactory;

#if NETCOREAPP3_0_OR_GREATER
                    await using var leasingScope = await leasingScopeFactory.CreateScopeAsync(
                        QueueKey.Scheduler,
                        TimeSpan.FromMinutes(1),
                        execToken
                    );
#else
                    using var leasingScope = await leasingScopeFactory.CreateScopeAsync(
                        QueueKey.Scheduler,
                        TimeSpan.FromMinutes(1),
                        execToken
                    );
#endif
                    if (leasingScope.Acquired)
                    {
                        var storage = scope.Storage;

                        var dueSchedules = await storage.GetDueSchedulesAsync(horizon, ioToken);
                        // ... foreach + UpdateSchedulesAsync ...
                    }
                    else
                    {
                        _logger.LogDebug("Could not acquire leasing scope for schedule poller");
                    }
```

**Replacement — new `ExecuteInLeaseAsync` call site (non-generic overload):**
```csharp
                    using var scope = _serviceScopeFactory.CreateScope();
                    var storage = scope.Storage;

                    await storage.ExecuteInLeaseAsync(QueueKey.Scheduler, async innerCt =>
                    {
                        var dueSchedules = await storage.GetDueSchedulesAsync(horizon, ioToken);

                        foreach (var schedule in dueSchedules)
                        {
                            if (schedule.PayloadType is null)
                            {
                                _logger.LogWarning(
                                    "Schedule {ScheduleKey} has no payload type defined, disabling schedule",
                                    schedule.JobKey
                                );
                                schedule.Disable(now);
                                continue;
                            }

                            await _scheduleProcessor.ProcessAsync(schedule, horizon, execToken);
                            schedule.UpdateNextOccurence(horizon, now);
                        }

                        await storage.UpdateSchedulesAsync(dueSchedules, execToken);
                    }, execToken);
```

**Critical structural notes:**
- Uses the **non-generic** `Task ExecuteInLeaseAsync(QueueKey, Func<CancellationToken, Task>, CancellationToken)` overload — no return value.
- The `#if NETCOREAPP3_0_OR_GREATER` / `#else` / `#endif` block is removed entirely.
- `scope.LeasingScopeFactory` access is removed — `leasingScopeFactory` variable disappears.
- `now` is already captured in the outer scope from `var now = _clock.UtcNow;` (line 44) — the callback closure captures it correctly.

---

### `src/Atomizer/Storage/InMemoryStorage.cs` (storage, add two stub methods)

**Analog:** Self — current content of `src/Atomizer/Storage/InMemoryStorage.cs`

**Existing method signature pattern to follow** (lines 27–29):
```csharp
    public Task<Guid> InsertAsync(AtomizerJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
```

**Two stub methods to append before the `// ---- helpers ----` comment (line 221):**
```csharp
    public Task<TResult> ExecuteInLeaseAsync<TResult>(
        QueueKey queue,
        Func<CancellationToken, Task<TResult>> callback,
        CancellationToken cancellationToken
    )
    {
        // TODO: Implemented in Phase 2
        throw new NotImplementedException();
    }

    public Task ExecuteInLeaseAsync(
        QueueKey queue,
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken
    )
    {
        // TODO: Implemented in Phase 2
        throw new NotImplementedException();
    }
```

No additional imports needed — `QueueKey` and `Func<,>` are already in scope.

---

### `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` (storage, add two stub methods)

**Analog:** Self — current content of `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs`

**Two stub methods to append after `GetDueSchedulesAsync` (line 239), before the closing `}`:**
```csharp
    public Task<TResult> ExecuteInLeaseAsync<TResult>(
        QueueKey queue,
        Func<CancellationToken, Task<TResult>> callback,
        CancellationToken cancellationToken
    )
    {
        // TODO: Implemented in Phase 4
        throw new NotImplementedException();
    }

    public Task ExecuteInLeaseAsync(
        QueueKey queue,
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken
    )
    {
        // TODO: Implemented in Phase 4
        throw new NotImplementedException();
    }
```

No additional imports needed.

---

### `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs` (factory, remove NoopLeasingScopeFactory reference)

**Analog:** Self — current content of `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs`

**Current non-relational fallback** (lines 34–38):
```csharp
        _logger.LogDebug("Database is not relational, using NoopLeasingScopeFactory for queue {QueueKey}", key);

        var noopLeasingScopeFactory = new NoopLeasingScopeFactory();
        return await noopLeasingScopeFactory.CreateScopeAsync(key, scopeTimeout, cancellationToken);
```

**Replacement — throw `NotSupportedException` for non-relational providers:**
```csharp
        _logger.LogDebug(
            "Database is not relational, leasing scope not supported for queue {QueueKey}",
            key
        );

        throw new NotSupportedException(
            "DatabaseTransactionLeasingScopeFactory requires a relational database provider."
        );
```

**Also remove** the `using Atomizer.Core;` import on line 3 if it was only there to reference `NoopLeasingScopeFactory`. Verify no other reference to `Atomizer.Core` types exists in the file first.

The class survives until Phase 5 — this is a minimal fix to remove the compile dependency on the deleted `NoopLeasingScopeFactory`.

---

### Deleted Files (no pattern needed — just `git rm`)

| File | Action |
|------|--------|
| `src/Atomizer/Abstractions/IAtomizerLeasingScope.cs` | `git rm` |
| `src/Atomizer/Abstractions/IAtomizerLeasingScopeFactory.cs` | `git rm` |
| `src/Atomizer/Core/NoopLeasingScopeFactory.cs` | `git rm` |
| `src/Atomizer/Configuration/LeasingScopeOptions.cs` | `git rm` |
| `tests/Atomizer.Tests/Core/NoopLeasingScopeFactoryTests.cs` | `git rm` |

---

### `tests/Atomizer.Tests/Processing/QueuePollerTests.cs` (test, remove leasing mocks)

**Analog:** Self — current content of `tests/Atomizer.Tests/Processing/QueuePollerTests.cs`

**Fields to remove from the class** (lines 16–18):
```csharp
        private readonly IAtomizerLeasingScopeFactory _leasingScopeFactory =
            Substitute.For<IAtomizerLeasingScopeFactory>();
        private readonly IAtomizerLeasingScope _leasingScope = Substitute.For<IAtomizerLeasingScope>();
```

**Constructor setup lines to remove** (lines 31–34):
```csharp
            _scope.LeasingScopeFactory.Returns(_leasingScopeFactory);
            _leasingScopeFactory
                .CreateScopeAsync(Arg.Any<QueueKey>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
                .Returns(_leasingScope);
```

**Per-test lines to remove:**
- `RunAsync_WhenJobsLeased_ShouldWriteToChannelAndLog` (line 59): `_leasingScope.Acquired.Returns(true);`
- `RunAsync_WhenNoJobsLeased_ShouldLogNoJobs` (line 84): `_leasingScope.Acquired.Returns(true);`

**New mock needed** — `_storage.ExecuteInLeaseAsync` must be set up so the callback is actually invoked. Use NSubstitute's `Returns` with a delegate:
```csharp
// Generic overload setup pattern (add to constructor or per-test):
_storage
    .ExecuteInLeaseAsync(
        Arg.Any<QueueKey>(),
        Arg.Any<Func<CancellationToken, Task<List<AtomizerJob>>>>(),
        Arg.Any<CancellationToken>()
    )
    .Returns(callInfo => callInfo.Arg<Func<CancellationToken, Task<List<AtomizerJob>>>>()(CancellationToken.None));
```

**Existing test that exercises exception path** (`RunAsync_WhenExceptionThrown_ShouldLogError`, lines 97–112): currently throws from `_scopeFactory.CreateScope()` — this pattern survives unchanged since the exception is thrown before `ExecuteInLeaseAsync` is reached.

**Existing logging assertions** (`_logger.Received().LogDebug(...)`) survive unchanged — the log messages in `QueuePoller` do not change.

---

### `tests/Atomizer.Tests/Scheduling/SchedulePollerTests.cs` (test, remove leasing mocks)

**Analog:** Self — current content of `tests/Atomizer.Tests/Scheduling/SchedulePollerTests.cs`

**`RunAsync_WhenDueSchedules_ShouldProcessSchedules` test local variables to remove** (lines 44–45, 47–48):
```csharp
        var leasingScopeFactory = Substitute.For<IAtomizerLeasingScopeFactory>();
        var leasingScope = Substitute.For<IAtomizerLeasingScope>();
        leasingScopeFactory
            .CreateScopeAsync(Arg.Any<QueueKey>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(leasingScope);
```

**Lines to remove from the same test** (lines 68–69):
```csharp
        scope.LeasingScopeFactory.Returns(leasingScopeFactory);
        leasingScope.Acquired.Returns(true);
```

**New mock needed** — `storage.ExecuteInLeaseAsync` (non-generic overload) must invoke the callback:
```csharp
// Non-generic overload setup pattern:
storage
    .ExecuteInLeaseAsync(
        Arg.Any<QueueKey>(),
        Arg.Any<Func<CancellationToken, Task>>(),
        Arg.Any<CancellationToken>()
    )
    .Returns(callInfo => callInfo.Arg<Func<CancellationToken, Task>>()(CancellationToken.None));
```

**`RunAsync_WhenExceptionThrown_ShouldLogError` test** (lines 105–122): currently throws from `scope.LeasingScopeFactory` access. After the refactor `scope.LeasingScopeFactory` no longer exists, so the exception trigger must change. Replace with:
```csharp
        scope.Storage.Returns(storage);
        storage
            .ExecuteInLeaseAsync(Arg.Any<QueueKey>(), Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("fail"));
```

The `scope.LeasingScopeFactory.Returns(...)` line (old trigger) is removed entirely; `scope.Storage.Returns(storage)` is added so `storage` is accessible before the throw.

---

## Shared Patterns

### File-scoped namespaces
**Source:** Every file in the project  
**Apply to:** All modified files  
```csharp
namespace Atomizer.Processing;   // not namespace Atomizer.Processing { }
```

### `internal sealed class` for implementations
**Source:** `src/Atomizer/Processing/QueuePoller.cs` line 13, `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` line 9  
**Apply to:** All implementation classes — no access modifier changes needed in this phase.

### `CancellationToken cancellationToken` as last parameter, no default on storage interface
**Source:** `src/Atomizer/Abstractions/IAtomizerStorage.cs` lines 11, 19, 29, 44  
**Apply to:** Both new `ExecuteInLeaseAsync` members — confirmed, no default value.

### XML documentation mandatory on public interface members
**Source:** `src/Atomizer/Abstractions/IAtomizerStorage.cs` lines 5–11 (canonical example)  
**Apply to:** Both new `ExecuteInLeaseAsync` members on `IAtomizerStorage` — `<summary>`, `<typeparam>` (generic only), `<param>` for each param, `<returns>`.  
`TreatWarningsAsErrors=true` means missing XML docs fail the build.

### Structured logging — no string interpolation
**Source:** `src/Atomizer/Processing/QueuePoller.cs` lines 80, 92, 97, 103  
**Apply to:** Any log statement added or retained in modified pollers.  
```csharp
_logger.LogDebug("Queue '{Queue}' leasing {JobCount} job(s)", queue.QueueKey, jobs.Count);
// NOT:
_logger.LogDebug($"Queue '{queue.QueueKey}' leasing {jobs.Count} job(s)");
```

### NSubstitute callback-invocation pattern for async delegates (tests)
**Source:** Existing `_leasingScopeFactory.CreateScopeAsync(...).Returns(_leasingScope)` pattern in test files  
**Apply to:** New `ExecuteInLeaseAsync` mock setup in `QueuePollerTests` and `SchedulePollerTests`  
```csharp
// Force NSubstitute to execute the callback delegate:
_storage
    .ExecuteInLeaseAsync(Arg.Any<QueueKey>(), Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
    .Returns(callInfo => callInfo.Arg<Func<CancellationToken, Task>>()(CancellationToken.None));
```

---

## No Analog Found

All files in Phase 1 have close analogs (they are all modifications of existing files). No net-new files are created in this phase.

---

## Metadata

**Analog search scope:** `src/Atomizer/`, `src/Atomizer.EntityFrameworkCore/`, `tests/Atomizer.Tests/`  
**Files scanned:** 19 source files read directly  
**Pattern extraction date:** 2026-05-03
