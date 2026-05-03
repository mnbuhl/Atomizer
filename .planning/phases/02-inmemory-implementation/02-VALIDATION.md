---
phase: 2
slug: inmemory-implementation
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-05-03
---

# Phase 2 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 (2.0.1) + AwesomeAssertions 9.1.0 + NSubstitute 5.3.0 |
| **Config file** | `tests/Atomizer.Tests/Atomizer.Tests.csproj` |
| **Quick run command** | `dotnet test tests/Atomizer.Tests/Atomizer.Tests.csproj --no-build -l "console;verbosity=minimal"` |
| **Full suite command** | `dotnet test --no-build -l "console;verbosity=minimal"` |
| **Estimated runtime** | ~10 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/Atomizer.Tests/Atomizer.Tests.csproj --no-build -l "console;verbosity=minimal"`
- **After every plan wave:** Run `dotnet test --no-build -l "console;verbosity=minimal"`
- **Before `/gsd-verify-work`:** Full suite must be green
- **Max feedback latency:** ~10 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 02-01-01 | 01 | 0 | INMEM-01, INMEM-02, INMEM-03 | — | N/A | unit | `dotnet test tests/Atomizer.Tests/ --filter "FullyQualifiedName~InMemoryStorageLeaseTests" --no-build` | ❌ W0 | ⬜ pending |
| 02-01-02 | 01 | 1 | INMEM-01, INMEM-02 | — | N/A | unit | `dotnet test tests/Atomizer.Tests/ --filter "FullyQualifiedName~InMemoryStorageLease" --no-build` | ❌ W0 | ⬜ pending |
| 02-01-03 | 01 | 1 | INMEM-03 | — | N/A | unit | `dotnet test tests/Atomizer.Tests/ --filter "FullyQualifiedName~InMemoryStorageLease" --no-build` | ❌ W0 | ⬜ pending |
| 02-01-04 | 01 | 1 | INMEM-01 | — | N/A | unit | `dotnet test tests/Atomizer.Tests/ --no-build -l "console;verbosity=minimal"` | ✅ | ⬜ pending |
| 02-02-01 | 02 | 2 | (existing) | — | N/A | unit | `dotnet test tests/Atomizer.Tests/ --no-build -l "console;verbosity=minimal"` | ✅ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/Atomizer.Tests/Storage/InMemoryStorageLeaseTests.cs` — new test file covering INMEM-01, INMEM-02, INMEM-03
  - Semaphore held for full callback duration
  - Concurrent callers serialized (second caller gets `default` / skips)
  - Exception inside callback releases semaphore
  - Non-acquired case returns `default!`
  - `QueueKey.Scheduler` uses the same lock as `UpsertScheduleAsync` (mutually exclusive)

*Existing infrastructure (xUnit v3, AwesomeAssertions, NSubstitute) is already present — no framework install needed.*

---

## Manual-Only Verifications

*All phase behaviors have automated verification.*

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 10s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
