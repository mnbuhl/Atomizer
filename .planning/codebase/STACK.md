# Technology Stack

**Analysis Date:** 2026-05-03

## Languages

**Primary:**
- C# (`LangVersion` 14 for `src/`, 12 for `tests/`) — all library, sample, and test code.

**Secondary:**
- PowerShell — operational scripts `migrations.ps1` and `test-migrations.ps1` at repo root.
- YAML — `compose.yml` for local dev databases.

## Runtime

**Target Frameworks:**
- `src/Atomizer/Atomizer.csproj` multi-targets `netstandard2.0`, `net8.0`, `net10.0`.
- `src/Atomizer.EntityFrameworkCore/Atomizer.EntityFrameworkCore.csproj` multi-targets `net6.0`, `net8.0`, `net10.0`.
- All test projects (`tests/Atomizer.Tests`, `tests/Atomizer.EntityFrameworkCore.Tests`, `tests/Atomizer.Tests.Utilities`) multi-target `net6.0`, `net8.0`, `net10.0`.
- Samples (`samples/Atomizer.Example`, `samples/Atomizer.EFCore.Example`) target `net10.0` using `Microsoft.NET.Sdk.Web`.

Note: Although the project docstring mentions `netstandard2.1`, the actual `TargetFrameworks` in `src/Atomizer/Atomizer.csproj` is `netstandard2.0;net8.0;net10.0`.

**Runtime Hosts:**
- ASP.NET Core / .NET Generic Host — consumed via `Microsoft.Extensions.Hosting.Abstractions` (`IHostedService` is the integration point for `AtomizerQueueService` and `AtomizerSchedulerService`).

**Package Manager:**
- NuGet, `PackageReference` style with per-project lockfiles (`RestorePackagesWithLockFile=true`).
- Lockfiles present: `src/Atomizer/packages.lock.json`, `src/Atomizer.EntityFrameworkCore/packages.lock.json`.

## Frameworks

**Core:**
- `Microsoft.Extensions.DependencyInjection.Abstractions` `[6.0.0, )` — DI primitives (`IServiceProvider`, `ServiceLifetime`).
- `Microsoft.Extensions.Hosting.Abstractions` `[6.0.0, )` — `IHostedService`, graceful shutdown hooks.
- `Microsoft.Extensions.Logging.Abstractions` `[6.0.0, )` — `ILogger<T>` throughout pipeline and storage.
- `System.Text.Json` `[6.0.0, )` — the only supported job payload serializer.
- `System.Threading.Channels` `[6.0.0, )` — `BoundedChannel<AtomizerJob>` inside `QueuePump` producer/consumer.
- `Cronos` `0.11.1` — cron-expression parsing for `Schedule` value object (seconds-level, 5- and 6-part cron).

**EF Core Storage (`src/Atomizer.EntityFrameworkCore`):**
- `Microsoft.EntityFrameworkCore` `6.0.0` — ORM base.
- `Microsoft.EntityFrameworkCore.Relational` `6.0.0` — raw SQL execution used by `EntityFrameworkCoreStorage`.

**Testing:**
- `xunit.v3` `2.0.1` — test runner (see `tests/Atomizer.Tests/Atomizer.Tests.csproj`).
- `xunit.runner.visualstudio` `3.0.2`.
- `Microsoft.NET.Test.Sdk` `17.13.0`.
- `AwesomeAssertions` `9.1.0` — assertion library.
- `NSubstitute` `5.3.0` — mocking library.
- `AutoFixture` `4.18.1` — test data generation (unit tests only).
- `coverlet.collector` `6.0.4` — coverage collection.
- `Testcontainers.MsSql`, `Testcontainers.MySql`, `Testcontainers.PostgreSql` `4.6.0` — per-provider integration test fixtures in `tests/Atomizer.EntityFrameworkCore.Tests/Fixtures/`.

**Build/Dev:**
- `Microsoft.NET.Sdk` (library/test projects) and `Microsoft.NET.Sdk.Web` (samples).
- CSharpier — formatting (`.csharpierrc`: `printWidth: 120`, `indentSize: 4`, `useTabs: false`). Run via `dotnet csharpier .`.
- Lockfile restore enforced (`RestorePackagesWithLockFile=true`).
- `TreatWarningsAsErrors=true` on all `src/` projects with `NU1901,NU1902,NU1903,NU1904` excepted (vulnerability warnings are non-blocking).
- `AnalysisLevel=latest`, `EnablePackageValidation=true` on both shipping projects.
- `ImplicitUsings=enable`, `Nullable=enable` across the codebase.

## Key Dependencies

**Critical (production shipping packages):**
- `Cronos` `0.11.1` — drives `Schedule.GetOccurrences()` and `UpdateNextOccurence` in `src/Atomizer/Scheduling/`.
- `System.Threading.Channels` `6.0.0` — backbone of the per-queue pump at `src/Atomizer/Processing/QueuePump.cs`.
- `System.Text.Json` `6.0.0` — only serializer; explicit convention (no Newtonsoft).
- `Microsoft.EntityFrameworkCore` `6.0.0` — minimum supported EF Core version for the storage package.

**Infrastructure/Dev only:**
- `Swashbuckle.AspNetCore` `9.0.3` — sample-only (Swagger UI in `samples/*/`).
- `Microsoft.EntityFrameworkCore.Design` `9.0.4` — sample-only for `dotnet ef migrations`.

## Configuration

**Package metadata (`src/Directory.Build.props`):**
- Author `Mikkel Buhl`.
- License `MIT` via `PackageLicenseExpression`.
- Repo `https://github.com/mnbuhl/Atomizer`.
- Package tags include `atomizer;queue;queueing;scheduler;scheduling;jobs;tasks;background`.
- `README.md` and `LICENSE` packed into NuGet output.

**Project-level:**
- `src/Atomizer/Atomizer.csproj` — `PackageId=Atomizer`, description "Atomizer provides background job queue and scheduler for .NET applications".
- `src/Atomizer.EntityFrameworkCore/Atomizer.EntityFrameworkCore.csproj` — `PackageId=Atomizer.EntityFrameworkCore`, appends `efcore;entityframeworkcore` to package tags.

**Runtime Configuration Entry Points:**
- `services.AddAtomizer(options => { ... })` — core registration (queues, handlers, scheduling, storage selector).
- `services.AddAtomizerProcessing(options => { ... })` — `StartupDelay`, `GracefulShutdownTimeout`.
- `options.UseInMemoryStorage()` — default in-process backend.
- `options.UseEntityFrameworkCoreStorage<TDbContext>()` — implemented in `src/Atomizer.EntityFrameworkCore/Extensions/AtomizerOptionsExtensions.cs`; registers `EntityFrameworkCoreStorage<TDbContext>` and `DatabaseTransactionLeasingScopeFactory<TDbContext>` as `Scoped`.
- `modelBuilder.AddAtomizerEntities(schema: "atomizer")` — from `src/Atomizer.EntityFrameworkCore/Extensions/ModelBuilderExtensions.cs`; required in consumer `DbContext.OnModelCreating`.

**Build config files:**
- `/Users/mbuhl/projects/Atomizer/Atomizer.sln`
- `/Users/mbuhl/projects/Atomizer/src/Directory.Build.props`
- `/Users/mbuhl/projects/Atomizer/.csharpierrc`
- `/Users/mbuhl/projects/Atomizer/compose.yml`
- `/Users/mbuhl/projects/Atomizer/migrations.ps1`
- `/Users/mbuhl/projects/Atomizer/test-migrations.ps1`

**Environment:**
- No `appsettings.json` required by the library; consumers provide DI, connection strings, and a `DbContext`.
- `compose.yml` honors `DB_ROOT_PASSWORD` env var (defaulting to `Passw0rd!`) for local Postgres/MySQL/MSSQL containers.

## Platform Requirements

**Development:**
- .NET SDK with support for `net10.0` (required to build `src/Atomizer` and the samples).
- Docker (for local `compose.yml` databases and Testcontainers-backed EF integration tests).
- Optional: PowerShell 7 to run `migrations.ps1` / `test-migrations.ps1`.

**Production:**
- Any .NET Standard 2.0+, .NET 8, or .NET 10 host for the core library.
- Any .NET 6, .NET 8, or .NET 10 host for the EF Core storage backend.
- A supported relational database (SqlServer, PostgreSQL, MySQL) when using EF Core storage; see `INTEGRATIONS.md`.

---

*Stack analysis: 2026-05-03*
