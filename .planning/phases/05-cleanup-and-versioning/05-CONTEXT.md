# Phase 5: Cleanup and Versioning - Context

**Gathered:** 2026-05-03
**Status:** Ready for planning

<domain>
## Phase Boundary

Make `DatabaseTransactionLeasingScope` internal sealed (access modifier only — no logic change), delete the dead `DatabaseTransactionLeasingScopeFactory<TDbContext>`, add `<GenerateDocumentationFile>true</GenerateDocumentationFile>` to both csproj files, and write XML documentation across all public API members in both packages until the build compiles clean. No version bump in this phase.

</domain>

<decisions>
## Implementation Decisions

### DatabaseTransactionLeasingScope

- **D-01:** Change `DatabaseTransactionLeasingScope` from `public class` to `internal sealed class` — access modifier change only. `StartTransaction` static method and `DisposeAsync` commit/rollback logic remain exactly as-is. The two call sites in `EntityFrameworkCoreStorage.ExecuteInLeaseAsync` (lines 274 and 303) continue to work without changes.
- **D-02:** Delete `DatabaseTransactionLeasingScopeFactory<TDbContext>` entirely — dead code, no DI registration, no callers outside its own file.

### XML Documentation Build Enforcement

- **D-03:** Add `<GenerateDocumentationFile>true</GenerateDocumentationFile>` to the `<PropertyGroup>` in both `src/Atomizer/Atomizer.csproj` and `src/Atomizer.EntityFrameworkCore/Atomizer.EntityFrameworkCore.csproj`. This causes CS1591 to fire as a build error (via existing `TreatWarningsAsErrors=true`) for any public member missing a `<summary>`.
- **D-04:** Fix all missing XML docs across the full public API of both packages — not just new/changed members from this milestone. A single pass; build must pass cleanly at the end.

### Version Bump

- Skipped — no published adopters; breaking changes are acceptable without a major version bump at this stage.

### Claude's Discretion

- Exact wording of XML doc summaries, param descriptions, and returns on all documented members — follow existing documentation style in files that already have docs.
- Whether to add `<NoWarn>` entries for generated types or `obj/` paths if the doc file generator picks up unexpected types — handle as needed to keep the build clean.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Requirements Governing This Phase
- `.planning/REQUIREMENTS.md` — COMPAT-03 governs Phase 5 (XML documentation on new/changed public API members); COMPAT-02 (version bump) is deferred
- `.planning/ROADMAP.md` — Phase 5 success criteria (3 items) are the acceptance test

### Files Being Changed
- `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScope.cs` — Change `public class` → `internal sealed class`; no other changes
- `src/Atomizer/Atomizer.csproj` — Add `<GenerateDocumentationFile>true</GenerateDocumentationFile>`
- `src/Atomizer.EntityFrameworkCore/Atomizer.EntityFrameworkCore.csproj` — Add `<GenerateDocumentationFile>true</GenerateDocumentationFile>`

### Files Being Deleted
- `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScopeFactory.cs` — Dead code; no callers, no DI registration

### Files Needing XML Doc Review (full public API)
- `src/Atomizer/Abstractions/IAtomizerClient.cs`
- `src/Atomizer/Abstractions/IAtomizerStorage.cs`
- `src/Atomizer/Abstractions/IAtomizerJob.cs`
- `src/Atomizer/Abstractions/IAtomizerServiceScope.cs`
- `src/Atomizer/Abstractions/IAtomizerJobSerializer.cs`
- `src/Atomizer/Configuration/AtomizerOptions.cs`
- `src/Atomizer/Configuration/QueueOptions.cs`
- `src/Atomizer/Configuration/SchedulingOptions.cs`
- `src/Atomizer/Configuration/AtomizerProcessingOptions.cs`
- `src/Atomizer/Configuration/JobStorageOptions.cs`
- `src/Atomizer/Configuration/ServiceCollectionExtensions.cs`
- `src/Atomizer/Models/AtomizerJob.cs`
- `src/Atomizer/Models/AtomizerSchedule.cs`
- `src/Atomizer/Models/AtomizerJobError.cs`
- `src/Atomizer/Models/Base/Model.cs`
- `src/Atomizer/Models/Base/ValueObject.cs`
- `src/Atomizer/Models/ValueObjects/QueueKey.cs`
- `src/Atomizer/Models/ValueObjects/JobKey.cs`
- `src/Atomizer/Models/ValueObjects/LeaseToken.cs`
- `src/Atomizer/Models/ValueObjects/RetryStrategy.cs`
- `src/Atomizer/Models/ValueObjects/Schedule.cs`
- `src/Atomizer/Models/ValueObjects/WorkerId.cs`
- `src/Atomizer/Exceptions/` (all exception types)
- `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreJobStorageOptions.cs`
- `src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs`
- `src/Atomizer.EntityFrameworkCore/Extensions/ModelBuilderExtensions.cs`

### Prior Phase Context (locked decisions)
- `.planning/phases/04-ef-core-implementation/04-CONTEXT.md` — D-01: `DatabaseTransactionLeasingScope` reused by `ExecuteInLeaseAsync`; Phase 5 makes it internal (not deleted); `DatabaseTransactionLeasingScopeFactory` deleted here

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `DatabaseTransactionLeasingScope.StartTransaction<TDbContext>(dbContext, timeout, ct)` — static factory at line 110; called at `EntityFrameworkCoreStorage.cs:274` and `:303`. Making the class `internal sealed` requires no changes to these call sites.
- Existing XML doc style on `IAtomizerClient.cs`, `IAtomizerStorage.cs`, `IAtomizerJob.cs` — use these as the documentation style reference for all new docs.

### Established Patterns
- `internal sealed class` — all processing/storage implementations already use this; `DatabaseTransactionLeasingScope` is the last holdout with `public class`.
- `TreatWarningsAsErrors=true` with `WarningsNotAsErrors=NU1901,NU1902,NU1903,NU1904` — adding `GenerateDocumentationFile` will make CS1591 a build error; no additional suppression needed unless obj/ artifacts surface.
- `<summary>` on every public member, `<param>` for every parameter, `<returns>` for non-void returns — established by CLAUDE.md XML documentation convention.

### Integration Points
- `EntityFrameworkCoreStorage.ExecuteInLeaseAsync` (both overloads) — only callers of `DatabaseTransactionLeasingScope.StartTransaction`; no changes needed there after the visibility change.
- EF Core extensions (`AtomizerOptionsExtensions.UseEntityFrameworkCoreStorage<TDbContext>`, `ModelBuilderExtensions.AddAtomizerEntities`) — public extension methods; need XML docs added if missing.

</code_context>

<specifics>
## Specific Ideas

- Changing `DatabaseTransactionLeasingScope` visibility is a one-line edit:
  ```csharp
  // Before
  public class DatabaseTransactionLeasingScope : IDisposable, IAsyncDisposable
  // After
  internal sealed class DatabaseTransactionLeasingScope : IDisposable, IAsyncDisposable
  ```

- `GenerateDocumentationFile` placement in csproj — add to the first `<PropertyGroup>` alongside `TreatWarningsAsErrors`:
  ```xml
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  ```

- Build verification: after all changes, `dotnet build` must produce zero warnings/errors on all target frameworks.

</specifics>

<deferred>
## Deferred Ideas

- **Major version bump** — no published adopters; breaking changes acceptable without a semver major bump at this stage. Revisit when a public release is planned.

</deferred>

---

*Phase: 5-Cleanup and Versioning*
*Context gathered: 2026-05-03*
