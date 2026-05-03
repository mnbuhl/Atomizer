---
phase: 1
slug: leasing-abstraction
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-05-03
---

# Phase 1 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 |
| **Config file** | tests/Atomizer.Tests/Atomizer.Tests.csproj |
| **Quick run command** | `dotnet build` |
| **Full suite command** | `dotnet test tests/Atomizer.Tests/Atomizer.Tests.csproj` |
| **Estimated runtime** | ~30 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet build`
- **After every plan wave:** Run `dotnet test tests/Atomizer.Tests/Atomizer.Tests.csproj`
- **Before `/gsd-verify-work`:** Full suite must be green
- **Max feedback latency:** 60 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 1-01-01 | 01 | 1 | LEASE-01 | — | N/A | build | `dotnet build src/Atomizer/Atomizer.csproj` | ✅ | ⬜ pending |
| 1-01-02 | 01 | 1 | LEASE-02 | — | N/A | build | `dotnet build` | ✅ | ⬜ pending |
| 1-01-03 | 01 | 1 | LEASE-03 | — | N/A | build | `dotnet build` | ✅ | ⬜ pending |
| 1-01-04 | 01 | 1 | COMPAT-01 | — | N/A | unit | `dotnet test tests/Atomizer.Tests/Atomizer.Tests.csproj` | ✅ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/Atomizer.Tests/Processing/QueuePollerTests.cs` — must compile after IAtomizerLeasingScopeFactory mocks removed
- [ ] `tests/Atomizer.Tests/Scheduling/SchedulePollerTests.cs` — must compile after IAtomizerLeasingScopeFactory mocks removed
- [ ] Delete `tests/Atomizer.Tests/Core/NoopLeasingScopeFactoryTests.cs` — class under test is deleted

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| `IAtomizerLeasingScopeFactory` absent from public API surface | LEASE-02 | Compile-time verification only | `dotnet build` — CS-errors if any file still references deleted types |
| `ExecuteInLeaseAsync` appears on `IAtomizerStorage` | LEASE-01 | Interface shape check | Read `IAtomizerStorage.cs` and confirm both overloads present with XML docs |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 60s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
