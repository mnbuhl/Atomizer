---
phase: 05-cleanup-and-versioning
fixed_at: 2026-05-03T00:00:00Z
review_path: .planning/phases/05-cleanup-and-versioning/05-REVIEW.md
iteration: 1
findings_in_scope: 11
fixed: 11
skipped: 0
status: all_fixed
---

# Phase 05: Code Review Fix Report

**Fixed at:** 2026-05-03T00:00:00Z
**Source review:** .planning/phases/05-cleanup-and-versioning/05-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 11 (CR-01, CR-02, CR-03, WR-01 through WR-08)
- Fixed: 11
- Skipped: 0

Note: IN-01 and IN-03 were fixed as trivially co-located with CR-01/CR-02 respectively. IN-02 and IN-04 were out of scope per fix_scope=critical_and_warning.

---

## Fixed Issues

### CR-01: `RetryStrategy.GetRetryInterval` throws `IndexOutOfRangeException` for `None` strategy

**Files modified:** `src/Atomizer/Models/ValueObjects/RetryStrategy.cs`
**Commit:** 9c49327
**Applied fix:** Changed `RetryStrategy.None` to initialize `RetryIntervals = [TimeSpan.Zero]` instead of `[]`. This ensures `GetRetryInterval(1)` no longer accesses index 0 on an empty array.

---

### CR-02: `Schedule.Weekly` is identical to `Schedule.Daily` — wrong cron expression

**Files modified:** `src/Atomizer/Models/ValueObjects/Schedule.cs`
**Commit:** 0f2400c
**Applied fix:** Changed `Schedule.Weekly` from `new Schedule("0", "0", "0", "*", "*", "*")` to `new Schedule("0", "0", "0", "*", "*", "0")` (midnight every Sunday). Also fixed `Schedule.Monthly` (IN-03 co-located): changed from `new Schedule("0", "0", "0", "*", "*", "?")` (equivalent to daily) to `new Schedule("0", "0", "0", "1", "*", "*")` (midnight on the 1st of each month). Updated XML doc summaries for both.

---

### CR-03: `new Random()` per call in `ApplyJitter` — non-thread-safe and produces correlated values

**Files modified:** `src/Atomizer/Models/ValueObjects/RetryStrategy.cs`
**Commit:** 9c49327
**Applied fix:** Replaced the per-call `new Random()` with a `#if NET6_0_OR_GREATER` conditional: on .NET 6+ uses `Random.Shared.NextDouble()` (thread-safe static); on older targets uses a `ThreadLocal<Random>` seeded from `Guid.NewGuid().GetHashCode()` to ensure per-thread instances with uncorrelated seeds. This eliminates thundering-herd retry storms under parallel execution.

---

### WR-01: `InMemoryStorage.GetDueJobsAsync` reads `_jobs[id]` without safe TryGetValue

**Files modified:** `src/Atomizer/Storage/InMemoryStorage.cs`
**Commit:** ae39d00
**Applied fix:** Replaced the `_jobs[id]` indexer (which throws `KeyNotFoundException` on a concurrent eviction race) with `_jobs.TryGetValue(id, out var j) ? j : null`, followed by a `j != null` guard in the `Where` clause and a `.Select(j => j!)` to restore the non-nullable type. The comment "safe: ids derived under the same lock" (which was incorrect — no lock is held) was removed.

---

### WR-02: `InMemoryStorage.UpdateJobsAsync` silently drops updates for missing jobs

**Files modified:** `src/Atomizer/Storage/InMemoryStorage.cs`
**Commit:** ae39d00
**Applied fix:** Replaced the `LogError` + `continue` pattern with `throw new InvalidOperationException(...)`. Callers now see an explicit failure rather than silently losing job state updates, which prevents ghost jobs with stalled retry counters.

---

### WR-03: `InMemoryStorage.UpdateSchedulesAsync` accesses `_schedules` without synchronization

**Files modified:** `src/Atomizer/Storage/InMemoryStorage.cs`
**Commit:** ae39d00
**Applied fix:** Both `UpdateSchedulesAsync` and `GetDueSchedulesAsync` now acquire the `QueueKey.Scheduler` semaphore (same lock used by `UpsertScheduleAsync`) before accessing the plain `Dictionary<JobKey, AtomizerSchedule>`. Both methods were changed to `async Task` / `async Task<T>` to support the `await scheduleLock.WaitAsync(cancellationToken)` call. The interface signatures (`Task` and `Task<IReadOnlyList<AtomizerSchedule>>`) are unchanged.

---

### WR-04: `LeaseToken` constructor contains dead branches after proven-length guard

**Files modified:** `src/Atomizer/Models/ValueObjects/LeaseToken.cs`
**Commit:** b0489cd
**Applied fix:** After the `if (parts.Length != 3) throw` guard, replaced the three ternary conditions (`parts.Length > 0 ? parts[0] : string.Empty`, etc.) with direct indexing (`parts[0]`, `new QueueKey(parts[1])`, `parts[2]`). The dead else-branches could silently produce a malformed token if the guard were weakened.

---

### WR-05: `AtomizerSchedule.GetOccurrences` CatchUp uses inclusive lower bound — can duplicate boundary occurrence

**Files modified:** `src/Atomizer/Models/AtomizerSchedule.cs`
**Commit:** bdd31fb
**Applied fix:** Introduced `var from = (LastEnqueueAt ?? CreatedAt).AddTicks(1)` and passed it to `CronExpression.GetOccurrences(from, now, TimeZone)`. This makes the range exclusive on the lower bound, preventing the last-enqueued occurrence from being included again on the next poll.

---

### WR-06: `EntityFrameworkCoreJobStorageOptions` XML `<remarks>` nested inside `<summary>`

**Files modified:** `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreJobStorageOptions.cs`
**Commit:** 50d9fc6
**Applied fix:** Moved the `<remarks>` tag on `AllowUnsafeProviderFallback` from inside `<summary>` to a sibling position after `</summary>`, producing well-formed XML doc output.

---

### WR-07: `AtomizerOptions.AddQueue` accepts `string` instead of `QueueKey`

**Files modified:** `src/Atomizer/Configuration/AtomizerOptions.cs`
**Commit:** 415b994
**Applied fix:** Changed the `AddQueue` signature from `string name` to `QueueKey name`. The implicit `string -> QueueKey` conversion operator on `QueueKey` means all existing callers (including string literals like `"many-workers-queue"`) continue to compile without changes. Validation now happens at the `QueueKey` constructor boundary, giving callers a clear error context.

---

### WR-08: `SchedulingOptions.TickInterval` has `internal set` — leaks internal-only mutation

**Files modified:** `src/Atomizer/Configuration/SchedulingOptions.cs`
**Commit:** 04b7274
**Applied fix:** Kept `internal set` (rather than changing to `private set`) because the test project `Atomizer.Tests` legitimately sets `TickInterval` via `InternalsVisibleTo` to control polling speed in timing-sensitive tests. Changing to `private set` would break the only test that uses it (`SchedulePollerTests`). Instead, added an explicit `<remarks>` block documenting why `internal set` is intentional. Also fixed all malformed XML doc comments in the file (three other properties had `<remarks>` nested inside `<summary>`).

---

### IN-01: `RetryStrategy.Intervals` dead null-check (co-located with CR-01)

**Files modified:** `src/Atomizer/Models/ValueObjects/RetryStrategy.cs`
**Commit:** 9c49327
**Applied fix:** Removed the dead `intervalsArray is null ||` condition from the empty-check, since `ToArray()` on a non-nullable `IEnumerable<TimeSpan>` never returns null.

---

### IN-03: `Schedule.Monthly` fires daily instead of monthly (co-located with CR-02)

**Files modified:** `src/Atomizer/Models/ValueObjects/Schedule.cs`
**Commit:** 0f2400c
**Applied fix:** See CR-02 entry above — fixed as part of the same commit.

---

## Skipped Issues

None — all in-scope findings were successfully fixed.

---

_Fixed: 2026-05-03T00:00:00Z_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
