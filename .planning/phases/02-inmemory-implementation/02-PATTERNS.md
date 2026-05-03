# Phase 2: InMemory Implementation - Pattern Map

**Mapped:** 2026-05-03
**Files analyzed:** 4 (1 modified source, 1 new test, 1 deleted test, 1 modified test)
**Analogs found:** 4 / 4

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `src/Atomizer/Storage/InMemoryStorage.cs` | storage/service | CRUD + event-driven (lease callback) | `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs` (semaphore pattern) | role-match |
| `tests/Atomizer.Tests/Storage/InMemoryStorageLeaseTests.cs` | test | — | `tests/Atomizer.Tests/Storage/InMemoryLeasingScopeFactoryTests.cs` | exact |
| `tests/Atomizer.Tests/Storage/InMemoryStorageTests.cs` | test (update) | — | itself (existing file, lines 46-50 need type update) | self |
| `tests/Atomizer.Tests/Storage/InMemoryLeasingScopeFactoryTests.cs` | test (delete) | — | — | n/a |

---

## Pattern Assignments

### `src/Atomizer/Storage/InMemoryStorage.cs` (storage, CRUD + lease-callback)

**Primary analog:** `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs`
**Secondary analog:** `src/Atomizer/Storage/InMemoryStorage.cs` (existing non-stub methods)

---

#### Field declarations pattern

Copy from `src/Atomizer/Storage/InMemoryStorage.cs` lines 10-14 and extend:

```csharp
// EXISTING (keep unchanged):
private readonly ConcurrentDictionary<Guid, AtomizerJob> _jobs = new();
private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, byte>> _leasesByToken = new();
private readonly Dictionary<JobKey, AtomizerSchedule> _schedules = new();  // D-08: plain dict safe under _scheduleLock

// REPLACE line 11:
// Before:
private readonly Dictionary<QueueKey, HashSet<Guid>> _queues = new(); // guarded per-queue
// After (D-07):
private readonly ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>> _queues = new();

// ADD new fields after existing field block (D-01, D-05 Design A):
private readonly ConcurrentDictionary<QueueKey, SemaphoreSlim> _semaphores = new();
// _scheduleLock: Design A — use _semaphores.GetOrAdd(QueueKey.Scheduler, ...) so UpsertScheduleAsync
// and ExecuteInLeaseAsync(QueueKey.Scheduler) share the SAME semaphore and are mutually exclusive.
```

---

#### Semaphore acquire pattern — zero-timeout (skip-on-busy)

**Source:** `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs` lines 61-79 (the `AcquireAsync` body)

The existing codebase already uses this exact `WaitAsync(TimeSpan.Zero, cancellationToken)` pattern:

```csharp
// From InMemoryLeasingScopeFactory.cs lines 61-79 — extract and adapt:
var (semaphore, acquiredTimestamp) = Semaphores.GetOrAdd(key, (new SemaphoreSlim(1, 1), acquiredAt));
var acquired = await semaphore.WaitAsync(TimeSpan.Zero, cancellationToken);
// ...
return new InMemoryLeasingScope(semaphore) { Acquired = acquired };
```

Adapted for `ExecuteInLeaseAsync<TResult>` (D-01, D-02, D-04):

```csharp
public async Task<TResult> ExecuteInLeaseAsync<TResult>(
    QueueKey queue,
    Func<CancellationToken, Task<TResult>> callback,
    CancellationToken cancellationToken
)
{
    var semaphore = _semaphores.GetOrAdd(queue, _ => new SemaphoreSlim(1, 1));
    var acquired = await semaphore.WaitAsync(TimeSpan.Zero, cancellationToken);
    if (!acquired)
    {
        _logger.LogDebug("ExecuteInLeaseAsync: skipping tick for queue '{QueueKey}' — lease already held", queue);
        return default!;
    }
    try
    {
        return await callback(cancellationToken);
    }
    finally
    {
        semaphore.Release();
    }
}
```

Key points:
- `semaphore.Release()` is only ever reached inside `finally` when `acquired == true` — the early return exits before `try`, so `Release()` is never called on a non-acquired semaphore.
- Return `default!` on miss (D-02). The caller `QueuePoller` initializes `leasedJobs = []` before calling, so the null-path via `default!` on `List<AtomizerJob>` (= `null`) would hit `leasedJobs.Count` at line 102 of `QueuePoller.cs`. Add a null-coalescing guard in `QueuePoller.RunAsync` line 58: `leasedJobs = await storage.ExecuteInLeaseAsync(...) ?? [];`

---

#### Non-generic delegation pattern (D-03)

**Source:** CONTEXT.md `<specifics>` block — confirmed consistent with `InMemoryLeasingScopeFactory.Dispose()` single-release pattern.

```csharp
public Task ExecuteInLeaseAsync(
    QueueKey queue,
    Func<CancellationToken, Task> callback,
    CancellationToken cancellationToken
) => ExecuteInLeaseAsync<bool>(queue, async ct => { await callback(ct); return true; }, cancellationToken);
```

---

#### Semaphore acquire pattern — always-wait (UpsertScheduleAsync, D-05 Design A, D-06)

**Source:** `src/Atomizer/Storage/InMemoryStorage.cs` lines 155-166 (current `UpsertScheduleAsync` body) — replace with locked version.

Design A: the `QueueKey.Scheduler` entry in `_semaphores` IS the schedule lock. `UpsertScheduleAsync` acquires it with `WaitAsync(cancellationToken)` (always waits — no timeout):

```csharp
public async Task<Guid> UpsertScheduleAsync(AtomizerSchedule schedule, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();

    var scheduleLock = _semaphores.GetOrAdd(QueueKey.Scheduler, _ => new SemaphoreSlim(1, 1));
    await scheduleLock.WaitAsync(cancellationToken);
    try
    {
        var now = _clock.UtcNow;
        schedule.CreatedAt = schedule.CreatedAt == default ? now : schedule.CreatedAt;
        schedule.UpdatedAt = now;
        _schedules[schedule.JobKey] = schedule;
        _logger.LogDebug("UpsertSchedule: upserted schedule for jobKey={JobKey}", schedule.JobKey);
        return schedule.Id;
    }
    finally
    {
        scheduleLock.Release();
    }
}
```

Note: `WaitAsync(cancellationToken)` (no timeout) always waits until available — correct for a client write path (D-06).

---

#### Inner collection helpers — IndexIntoQueue / UnindexFromQueue

**Source:** `src/Atomizer/Storage/InMemoryStorage.cs` lines 243-263

Replace `HashSet<Guid>` operations with `ConcurrentDictionary<Guid, byte>` idiom (D-07):

```csharp
// BEFORE (lines 243-251):
private void IndexIntoQueue(AtomizerJob job)
{
    if (!_queues.TryGetValue(job.QueueKey, out var ids))
    {
        ids = new HashSet<Guid>();
        _queues[job.QueueKey] = ids;
    }
    ids.Add(job.Id);
}

// AFTER:
private void IndexIntoQueue(AtomizerJob job)
{
    var ids = _queues.GetOrAdd(job.QueueKey, _ => new ConcurrentDictionary<Guid, byte>());
    ids[job.Id] = 0;
}

// BEFORE (lines 253-263):
private void UnindexFromQueue(AtomizerJob job)
{
    if (_queues.TryGetValue(job.QueueKey, out var ids))
    {
        ids.Remove(job.Id);
        if (ids.Count == 0)
        {
            _queues.Remove(job.QueueKey);
        }
    }
}

// AFTER (note: do NOT remove empty outer key — TOCTOU race, see RESEARCH.md Pitfall 3):
private void UnindexFromQueue(AtomizerJob job)
{
    if (_queues.TryGetValue(job.QueueKey, out var ids))
    {
        ids.TryRemove(job.Id, out _);
    }
}
```

---

#### GetDueJobsAsync inner-collection read (line 93)

**Source:** `src/Atomizer/Storage/InMemoryStorage.cs` lines 87-119

The `.Select(id => _jobs[id])` read of the inner set must be updated to iterate `ConcurrentDictionary` keys:

```csharp
// BEFORE line 87:
if (!_queues.TryGetValue(queueKey, out var ids) || ids.Count == 0)

// AFTER (same check, compatible because ConcurrentDictionary.Count works the same):
if (!_queues.TryGetValue(queueKey, out var ids) || ids.IsEmpty)

// BEFORE line 93:
candidates = ids.Select(id => _jobs[id])

// AFTER (iterate Keys of ConcurrentDictionary<Guid, byte>):
candidates = ids.Keys.Select(id => _jobs[id])
```

---

#### Logging pattern

**Source:** `src/Atomizer/Storage/InMemoryStorage.cs` lines 35-40, 65, 88-90, 147-151

Follow the established structured-logging style — placeholder names, no string interpolation:

```csharp
// Skip-tick log (new):
_logger.LogDebug("ExecuteInLeaseAsync: skipping tick for queue '{QueueKey}' — lease already held", queue);

// Compare with existing pattern at line 88-90:
_logger.LogDebug("LeaseBatch: queue {QueueKey} is empty", queueKey);

// Error pattern at line 56-58:
_logger.LogError("Update requested for missing job {JobId}", job.Id);
```

---

### `tests/Atomizer.Tests/Storage/InMemoryStorageLeaseTests.cs` (new test file)

**Analog:** `tests/Atomizer.Tests/Storage/InMemoryLeasingScopeFactoryTests.cs` (full file, 207 lines)

This file is the structural template for the new lease test file. Copy its class scaffolding patterns exactly.

---

#### Test class setup pattern

**Source:** `tests/Atomizer.Tests/Storage/InMemoryLeasingScopeFactoryTests.cs` lines 6-26

```csharp
// File header — no `using` directives needed (globals.cs covers AwesomeAssertions, NSubstitute, Xunit)
using System.Collections.Concurrent;
using Atomizer.Core;
using Atomizer.Storage;

namespace Atomizer.Tests.Storage;

/// <summary>
/// Unit tests for <see cref="InMemoryStorage"/> lease behaviour.
/// </summary>
public sealed class InMemoryStorageLeaseTests
{
    private static QueueKey NewKey() => new QueueKey($"q-{Guid.NewGuid():N}");

    private static (InMemoryStorage sut, IAtomizerClock clock) CreateSut(DateTimeOffset now)
    {
        var clock = Substitute.For<IAtomizerClock>();
        clock.UtcNow.Returns(now);
        clock.MinValue.Returns(DateTimeOffset.MinValue);
        clock.MaxValue.Returns(DateTimeOffset.MaxValue);
        var logger = Substitute.For<TestableLogger<InMemoryStorage>>();
        var options = new InMemoryJobStorageOptions { AmountOfJobsToRetainInMemory = 100 };
        return (new InMemoryStorage(options, clock, logger), clock);
    }
```

---

#### Concurrent-callers-serialized test pattern

**Source:** `tests/Atomizer.Tests/Storage/InMemoryLeasingScopeFactoryTests.cs` lines 53-67 (already-acquired returns false)

```csharp
// Translate the scope pattern to callback pattern:
[Fact]
public async Task ExecuteInLeaseAsync_WhenAlreadyAcquired_ShouldSkipCallback()
{
    // Arrange
    var t0 = DateTimeOffset.UtcNow;
    var (sut, _) = CreateSut(t0);
    var key = NewKey();
    var innerCallCount = 0;

    // Hold the semaphore by starting (but not completing) a first call
    var firstCallStarted = new TaskCompletionSource<bool>();
    var firstCallRelease = new TaskCompletionSource<bool>();

    var firstTask = sut.ExecuteInLeaseAsync<int>(
        key,
        async ct =>
        {
            firstCallStarted.SetResult(true);
            await firstCallRelease.Task;
            return 1;
        },
        CancellationToken.None
    );

    await firstCallStarted.Task;

    // Act — second caller should skip
    var result = await sut.ExecuteInLeaseAsync<int>(
        key,
        ct => { innerCallCount++; return Task.FromResult(99); },
        CancellationToken.None
    );

    // Assert
    result.Should().Be(default(int), "second caller sees lease held — returns default");
    innerCallCount.Should().Be(0, "second callback must not be invoked");

    // Cleanup
    firstCallRelease.SetResult(true);
    await firstTask;
}
```

---

#### Exception-releases-semaphore test pattern

**Source:** `tests/Atomizer.Tests/Storage/InMemoryLeasingScopeFactoryTests.cs` lines 98-115 (dispose-releases pattern)

```csharp
[Fact]
public async Task ExecuteInLeaseAsync_WhenCallbackThrows_ShouldReleaseSemaphore()
{
    // Arrange
    var t0 = DateTimeOffset.UtcNow;
    var (sut, _) = CreateSut(t0);
    var key = NewKey();

    // Act — first call throws
    var act = async () => await sut.ExecuteInLeaseAsync<int>(
        key,
        _ => throw new InvalidOperationException("boom"),
        CancellationToken.None
    );
    await act.Should().ThrowAsync<InvalidOperationException>();

    // Assert — semaphore released; subsequent call must acquire
    var result = await sut.ExecuteInLeaseAsync<int>(key, _ => Task.FromResult(42), CancellationToken.None);
    result.Should().Be(42, "semaphore must be released by finally block even when callback throws");
}
```

---

#### `QueueKey.Scheduler` / `UpsertScheduleAsync` mutual-exclusion test pattern

```csharp
[Fact]
public async Task UpsertScheduleAsync_AndExecuteInLeaseAsyncScheduler_ShouldBeMutuallyExclusive()
{
    // Arrange: both operations should share the same semaphore (Design A)
    // Hold ExecuteInLeaseAsync(QueueKey.Scheduler) while UpsertScheduleAsync is called concurrently.
    // Verify UpsertScheduleAsync blocks until the lease callback completes.
}
```

---

#### Field-introspection pattern for semaphore state

**Source:** `tests/Atomizer.Tests/Storage/InMemoryStorageTests.cs` lines 41-50 — use `NonPublicSpy.GetFieldValue`

```csharp
// Read _semaphores to verify semaphore count after operations:
var semaphores = NonPublicSpy.GetFieldValue<InMemoryStorage, ConcurrentDictionary<QueueKey, SemaphoreSlim>>(
    "_semaphores",
    sut
);
semaphores[key].CurrentCount.Should().Be(1, "semaphore must be released after callback completes");
```

---

### `tests/Atomizer.Tests/Storage/InMemoryStorageTests.cs` (modification — type assertion update)

**Source:** `tests/Atomizer.Tests/Storage/InMemoryStorageTests.cs` lines 46-50

Three assertions reference the old `_queues` type. Update each:

```csharp
// BEFORE (lines 46-50):
var queues = NonPublicSpy.GetFieldValue<InMemoryStorage, Dictionary<QueueKey, HashSet<Guid>>>(
    "_queues",
    _sut
);
queues[QueueKey.Default].Should().Contain(job.Id);

// AFTER:
var queues = NonPublicSpy.GetFieldValue<InMemoryStorage, ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>>>(
    "_queues",
    _sut
);
queues[QueueKey.Default].Should().ContainKey(job.Id);
```

The `_schedules` field type (`Dictionary<JobKey, AtomizerSchedule>`) is unchanged — assertions at lines 215-220 and 247-251 require no update.

---

### `tests/Atomizer.Tests/Storage/InMemoryLeasingScopeFactoryTests.cs` (delete)

No pattern extraction needed. File is deleted entirely (D-10). It references `InMemoryLeasingScopeFactory` which is dead code after Phase 1.

---

### `src/Atomizer/Processing/QueuePoller.cs` (one-line null-guard fix)

**Source:** `src/Atomizer/Processing/QueuePoller.cs` lines 45-46

`leasedJobs` is initialized as `List<AtomizerJob> leasedJobs = []` before the try block. After `ExecuteInLeaseAsync` returns `default!` (= `null` for `List<T>`) on a missed lease, `leasedJobs.Count` at line 102 would throw `NullReferenceException`.

```csharp
// BEFORE (line 45-46):
List<AtomizerJob> leasedJobs = [];

// AFTER — null-coalescing at the assignment site (line 58):
leasedJobs = await storage.ExecuteInLeaseAsync(
    queue.QueueKey,
    async innerCt => { ... },
    ct
) ?? [];
```

This keeps the `default!` contract on `ExecuteInLeaseAsync` intact while guarding the caller.

---

## Shared Patterns

### SemaphoreSlim try/finally release
**Source:** `src/Atomizer/Storage/InMemoryLeasingScopeFactory.cs` lines 82-96
**Apply to:** `ExecuteInLeaseAsync<TResult>` and `UpsertScheduleAsync`

The `Dispose()` method in `InMemoryLeasingScope` is the canonical semaphore-release guard:

```csharp
// From InMemoryLeasingScopeFactory.cs lines 82-96:
public void Dispose()
{
    if (!_released && Acquired)
    {
        _released = true;
        try
        {
            _semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // Ignore if the semaphore is already at max count
        }
    }
}
```

In `ExecuteInLeaseAsync`, the `_released` guard is unnecessary (the early return means `finally` is only reached when `acquired == true`), but the `try/catch SemaphoreFullException` on `Release()` is defensive-coding precedent in this codebase — apply it in the `finally` block.

### CancellationToken guard at method entry
**Source:** `src/Atomizer/Storage/InMemoryStorage.cs` lines 29, 49, 76, 124, 157, 170, 195
**Apply to:** Both `ExecuteInLeaseAsync` overloads and updated `UpsertScheduleAsync`

```csharp
cancellationToken.ThrowIfCancellationRequested();
```

All `InMemoryStorage` methods begin with this guard. Both new method bodies must include it before any `await`.

### Structured logging — Debug level for non-error paths
**Source:** `src/Atomizer/Storage/InMemoryStorage.cs` lines 35-40, 78-83, 112-117, 147-151
**Apply to:** Skip-tick log in `ExecuteInLeaseAsync`, completion log in `UpsertScheduleAsync`

```csharp
// Property names used in this file: {JobId}, {QueueKey}, {Count}, {JobKey}
// Template: use single quotes around queue name strings per existing style (lines 88-90)
_logger.LogDebug("LeaseBatch: queue {QueueKey} is empty", queueKey);
```

### Test file imports and globals
**Source:** `tests/Atomizer.Tests/globals.cs` lines 1-4 and `tests/Atomizer.Tests/Storage/InMemoryStorageTests.cs` lines 1-4

The global `using` covers `AwesomeAssertions`, `NSubstitute`, `Xunit`, and `Atomizer.Tests.Utilities`. New test files only need to add non-globally-imported namespaces:

```csharp
// From InMemoryLeasingScopeFactoryTests.cs lines 1-3:
using Atomizer.Core;
using Atomizer.Storage;

namespace Atomizer.Tests.Storage;
```

---

## No Analog Found

All files in this phase have close analogs. No new framework integrations or novel patterns are introduced.

| File | Role | Data Flow | Reason |
|------|------|-----------|--------|
| — | — | — | — |

---

## Metadata

**Analog search scope:** `src/Atomizer/Storage/`, `src/Atomizer/Processing/`, `src/Atomizer/Abstractions/`, `tests/Atomizer.Tests/Storage/`, `tests/Atomizer.Tests.Utilities/`
**Files scanned:** 7 source files read directly
**Pattern extraction date:** 2026-05-03
