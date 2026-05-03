# Retrospective

## Milestone: v1.0 — Storage Refactor

**Shipped:** 2026-05-03
**Phases:** 5 | **Plans:** 17 | **Commits:** 227

### What Was Built

1. Callback-based `ExecuteInLeaseAsync` contract on `IAtomizerStorage` — replaces two-step acquire/release leasing; both InMemory and EF Core backends implement it
2. InMemory `SemaphoreSlim` per-queue locking held for full callback duration; scheduler semaphore makes `UpsertScheduleAsync` atomic
3. `ISqlDialect` strategy pattern — provider SQL extracted from storage class into three `internal sealed` dialect classes; zero inline provider branching remains
4. EF Core `ReadCommitted` transaction spans `GetDueJobsAsync` + `UpdateJobsAsync` — `FOR UPDATE SKIP LOCKED` / `WITH (UPDLOCK, READPAST, ROWLOCK)` prevent double-dispatch
5. Native per-provider upsert eliminates the `@todo` race condition in `UpsertScheduleAsync`
6. Full XML documentation on all public APIs; `GenerateDocumentationFile=true`; clean build under `TreatWarningsAsErrors=true`

### What Worked

- **Strict dependency ordering**: Phases 1 → 2 → 3 → 4 → 5 ensured each phase could verify independently without placeholders. Phase 1 establishing the interface stubs let phases 2–4 work in isolation.
- **Integration checker**: Catching the `queue` parameter being silently ignored in `EntityFrameworkCoreStorage.ExecuteInLeaseAsync` would have been missed without the cross-phase agent. Cross-phase wiring checks added real value.
- **Retroactive VERIFICATION.md for Phase 3**: Structural phases (pure refactors) don't need conversational UAT — programmatic verification against grep + test run is the right tool and took under 10 minutes.
- **COMPAT-02 scope cut**: Recognizing mid-archival that the version bump was never the goal (no NuGet publish planned) and dropping it cleanly rather than patching it in saved work and kept the milestone honest.

### What Was Inefficient

- **ROADMAP progress table never updated during execution**: All five phases showed stale statuses at milestone close. The executor agents focused correctly on code, but a lightweight post-phase hook to update the progress row would eliminate this every time.
- **VALIDATION.md files created as drafts and never signed off**: All five VALIDATION.md files had `nyquist_compliant: false` at close. These were planning artifacts that were never revisited. Either sign off during execution or don't create them until needed.
- **Phase 3 missing VERIFICATION.md at execution time**: The executor agent did not produce a verification file for a structural phase. Retroactive verification worked but added a step at milestone close. Structural phases need a lightweight VERIFICATION.md template.

### Patterns Established

- **Callback-based storage contracts**: The `ExecuteInLeaseAsync(queue, callback, ct)` pattern is the established idiom for any future backend (MongoDB, Redis). The callback receives an open lock; the backend decides what "lock" means.
- **`ISqlDialect` as extension point**: New SQL providers require only implementing `ISqlDialect` and adding one case to `RelationalProviderCache.CreateDialect()`. No storage class changes.
- **Integration checker as a phase-close gate**: Worth running at every multi-phase milestone, not just for code review. Catches cross-cutting behavioral divergences (like the `queue` param issue) that phase-level tests miss.

### Key Lessons

- Structural refactor phases (rename, extract, wire) benefit more from grep-based verification than conversational UAT. Route them to programmatic checks earlier.
- Phase verifications should be produced by the executor agent as part of execution, not deferred to milestone close. Missing VERIFICATIONs are discovered too late.
- Dropping a requirement mid-archival is fine and correct — the audit gate exists precisely to surface scope drift before finalizing.

### Cost Observations

- Sessions: Multiple across 2025-08-09 → 2026-05-03 timeline
- Notable: Parallel wave execution (Phase 4 Wave 1 plans ran in parallel worktrees) significantly reduced clock time for EF Core implementation

---

## Cross-Milestone Trends

*(Will be updated after v1.1+)*

| Metric | v1.0 |
|--------|------|
| Phases | 5 |
| Plans | 17 |
| Requirements satisfied | 19/19 active |
| Tech debt items at close | 7 |
| Nyquist-compliant phases | 0/5 |
| Integration issues caught by checker | 1 (queue param) |
