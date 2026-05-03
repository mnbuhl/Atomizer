---
phase: 3
slug: sql-dialect-strategy
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-05-03
---

# Phase 3 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xunit.v3 2.0.1 |
| **Config file** | `tests/Atomizer.EntityFrameworkCore.Tests/xunit.runner.json` |
| **Quick run command** | `dotnet test tests/Atomizer.EntityFrameworkCore.Tests/Atomizer.EntityFrameworkCore.Tests.csproj --filter "FullyQualifiedName~Dialect"` |
| **Full suite command** | `dotnet test tests/Atomizer.EntityFrameworkCore.Tests/Atomizer.EntityFrameworkCore.Tests.csproj` |
| **Estimated runtime** | ~30 seconds (dialect unit tests; no containers) |

---

## Sampling Rate

- **After every task commit:** Run `dotnet build src/Atomizer.EntityFrameworkCore`
- **After every plan wave:** Run `dotnet test tests/Atomizer.EntityFrameworkCore.Tests/Atomizer.EntityFrameworkCore.Tests.csproj --filter "FullyQualifiedName~Dialect"`
- **Before `/gsd-verify-work`:** Full suite must be green
- **Max feedback latency:** ~30 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| ISqlDialect interface | 01 | 1 | DIAL-01 | — | N/A | unit (compile) | `dotnet build src/Atomizer.EntityFrameworkCore` | ❌ W0 | ⬜ pending |
| PostgreSqlDialect rename | 01 | 1 | DIAL-02 | — | N/A | unit | `dotnet test … --filter "FullyQualifiedName~PostgreSqlDialect"` | ❌ W0 | ⬜ pending |
| SqlServerDialect rename | 01 | 1 | DIAL-02 | — | N/A | unit | `dotnet test … --filter "FullyQualifiedName~SqlServerDialect"` | ❌ W0 | ⬜ pending |
| MySqlDialect rename | 01 | 1 | DIAL-02 | — | N/A | unit | `dotnet test … --filter "FullyQualifiedName~MySqlDialect"` | ❌ W0 | ⬜ pending |
| RelationalProviderCache.Dialect property | 01 | 1 | DIAL-01, DIAL-03 | — | N/A | unit (compile) | `dotnet build src/Atomizer.EntityFrameworkCore` | ❌ W0 | ⬜ pending |
| EntityFrameworkCoreStorage call sites | 02 | 2 | DIAL-03 | — | N/A | structural (grep) | `grep -r "RawSqlProvider" src/ \| wc -l` (must be 0) | ✅ (grep) | ⬜ pending |
| No inline provider branching in Storage | 02 | 2 | DIAL-04 | — | N/A | structural (grep) | `grep -n "DatabaseProvider" src/Atomizer.EntityFrameworkCore/Storage/` (must be 0 hits) | ✅ (grep) | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `[assembly: InternalsVisibleTo("Atomizer.EntityFrameworkCore.Tests")]` in `src/Atomizer.EntityFrameworkCore/` — prerequisite for all dialect tests to compile against `internal sealed` dialect classes
- [ ] `tests/Atomizer.EntityFrameworkCore.Tests/Providers/PostgreSqlDialectTests.cs` — covers DIAL-02 (Postgres SQL keywords)
- [ ] `tests/Atomizer.EntityFrameworkCore.Tests/Providers/SqlServerDialectTests.cs` — covers DIAL-02 (SQL Server SQL keywords)
- [ ] `tests/Atomizer.EntityFrameworkCore.Tests/Providers/MySqlDialectTests.cs` — covers DIAL-02 (MySQL SQL keywords)

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| `EntityFrameworkCoreStorage` delegates all SQL through `_providerCache.Dialect` with no inline branching | DIAL-03, DIAL-04 | Structural — grep confirms zero `RawSqlProvider` and zero `DatabaseProvider` references in storage class | `grep -rn "RawSqlProvider" src/Atomizer.EntityFrameworkCore/Storage/` and `grep -n "DatabaseProvider" src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` — both must return no hits |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
