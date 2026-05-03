# External Integrations

**Analysis Date:** 2026-05-03

## APIs & External Services

Atomizer is an in-process library; it does not call third-party HTTP APIs. The integration surface is restricted to .NET extension points (DI, logging, hosting) and relational databases via EF Core.

**Extension points consumed:**
- `IHostedService` (Microsoft.Extensions.Hosting) — `AtomizerQueueService`, `AtomizerSchedulerService` register as hosted services.
- `IServiceProvider` / `ServiceLifetime` (Microsoft.Extensions.DependencyInjection) — handler/storage/leasing scope resolution.
- `ILogger<T>` (Microsoft.Extensions.Logging) — pervasive structured logging.

## Data Storage

**Databases (EF Core backend — `src/Atomizer.EntityFrameworkCore`):**

Provider detection happens at runtime in `src/Atomizer.EntityFrameworkCore/Providers/RelationalProviderCache.cs` by inspecting `DbContext.Database.ProviderName`. The `DatabaseProvider` enum in `src/Atomizer.EntityFrameworkCore/Providers/DatabaseProvider.cs` lists `PostgreSql`, `MySql`, `SqlServer`, `Oracle`, `Sqlite`, `Unknown`.

Supported (raw-SQL provider exists under `src/Atomizer.EntityFrameworkCore/Providers/Sql/`):
- **PostgreSQL** — `Npgsql.EntityFrameworkCore.PostgreSQL`. Raw SQL provider: `src/Atomizer.EntityFrameworkCore/Providers/Sql/PostgreSqlProvider.cs`. Uses `FOR NO KEY UPDATE SKIP LOCKED` for atomic lease acquisition.
- **SQL Server** — `Microsoft.EntityFrameworkCore.SqlServer`. Raw SQL provider: `src/Atomizer.EntityFrameworkCore/Providers/Sql/SqlServerProvider.cs`.
- **MySQL** — `Pomelo.EntityFrameworkCore.MySql` or `MySql.EntityFrameworkCore`. Raw SQL provider: `src/Atomizer.EntityFrameworkCore/Providers/Sql/MySqlProvider.cs`.

Not first-party supported (provider detected, but no raw-SQL implementation):
- **SQLite** — detected as `Microsoft.EntityFrameworkCore.Sqlite`; used only in tests (`tests/Atomizer.EntityFrameworkCore.Tests/Fixtures/SqliteDatabaseFixture.cs`).
- **Oracle** — detected as `Oracle.EntityFrameworkCore` but commit `780267c` removed first-party support; the enum member remains.
- **Unknown providers** — throw unless `EntityFrameworkCoreJobStorageOptions.AllowUnsafeProviderFallback = true` (see `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreJobStorageOptions.cs`).

**Entity Framework Core integration details:**
- Storage implementation: `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` — wires `IAtomizerStorage` to EF Core.
- Leasing scope: `src/Atomizer.EntityFrameworkCore/Storage/DatabaseTransactionLeasingScope.cs` and `DatabaseTransactionLeasingScopeFactory.cs` — wrap a `ReadCommitted` `IDbContextTransaction` to model distributed locks.
- Entities: `AtomizerJobEntity`, `AtomizerJobErrorEntity`, `AtomizerScheduleEntity` under `src/Atomizer.EntityFrameworkCore/Entities/`.
- Mapping: `modelBuilder.AddAtomizerEntities(schema: "atomizer")` via `src/Atomizer.EntityFrameworkCore/Extensions/ModelBuilderExtensions.cs`.
- EF Core minimum version pinned at `6.0.0` (see `src/Atomizer.EntityFrameworkCore/packages.lock.json`).

**InMemory backend (`src/Atomizer/Storage/`):**
- `ConcurrentDictionary<Guid, AtomizerJob>` job store; `HashSet<Guid>` per-queue indexes.
- Evicts completed/failed jobs beyond `AmountOfJobsToRetainInMemory` (default 100).
- Leasing: `InMemoryLeasingScopeFactory` with per-queue `SemaphoreSlim(1,1)` and stale-lock timeout detection.

**File Storage:**
- Not applicable — framework is in-memory plus relational.

**Caching:**
- None external. `RelationalProviderCache` uses a process-local `ConcurrentDictionary<DatabaseProvider, RelationalProviderCache>` to cache per-provider raw SQL builders.

## Authentication & Identity

- Not applicable — Atomizer is a library with no auth surface. It inherits the consumer application's identity/connection security via the injected `DbContext`.

## Monitoring & Observability

**Error Tracking:**
- None built-in; consumers wire their own via `ILogger`. Job failures are recorded as `AtomizerJobErrorEntity` rows when using EF Core storage.

**Logs:**
- Structured logging via `Microsoft.Extensions.Logging.Abstractions`; `ILogger<T>` injected throughout processing, scheduling, and storage.
- Test helper `TestableLogger` at `tests/Atomizer.Tests.Utilities/TestableLogger.cs` for asserting log output.

**Metrics / Tracing:**
- None built-in. No `System.Diagnostics.DiagnosticSource`/`Activity` instrumentation is registered by Atomizer itself (the package flows in transitively via EF Core only).

## CI/CD & Deployment

**Hosting:**
- Distributed via NuGet (`PackageId=Atomizer`, `PackageId=Atomizer.EntityFrameworkCore`). Consumers host in their own ASP.NET Core or Generic Host process.

**CI Pipeline:**
- Not detected in this analysis scope (no `.github/workflows`, `.gitlab-ci.yml`, or `azure-pipelines.yml` inspected here).

**Local Database Orchestration:**
- `compose.yml` at repo root provisions Postgres 17, MySQL 8.0, and MSSQL 2022 containers on ports 5432/3306/1433 for local development.
- `migrations.ps1` / `test-migrations.ps1` PowerShell scripts drive EF Core migration workflows across providers.

**Integration Test Containers:**
- `Testcontainers.MsSql`, `Testcontainers.MySql`, `Testcontainers.PostgreSql` (`4.6.0`) spin per-fixture containers under `tests/Atomizer.EntityFrameworkCore.Tests/Fixtures/`:
  - `PostgreSqlDatabaseFixture.cs`
  - `MySqlDatabaseFixture.cs`
  - `SqlServerDatabaseFixture.cs`
  - `SqliteDatabaseFixture.cs` (no container — uses in-process SQLite)
  - Base: `BaseDatabaseFixture.cs`

## Environment Configuration

**Required env vars:**
- None for library consumers. The library relies on the injected `DbContext`'s own connection configuration.

**Local-dev env vars (optional):**
- `DB_ROOT_PASSWORD` — consumed by `compose.yml`; defaults to `Passw0rd!`.

**Secrets location:**
- No secrets managed by Atomizer. Credentials live in the consumer application's own configuration (connection strings passed to the `DbContext`).

## Webhooks & Callbacks

**Incoming:**
- None. No HTTP endpoints are exposed by Atomizer.

**Outgoing:**
- None. Atomizer does not initiate outbound HTTP traffic.

## External Library Integrations (summary)

| Library | Version | Where integrated | Purpose |
|---------|---------|------------------|---------|
| `Cronos` | 0.11.1 | `src/Atomizer/Scheduling/Schedule.cs`, `src/Atomizer/Scheduling/ScheduleProcessor.cs` | Cron expression parsing (5- and 6-part, seconds-level) |
| `System.Text.Json` | 6.0.0 | `src/Atomizer/Abstractions/IAtomizerJobSerializer.cs` impl | Job payload (de)serialization |
| `System.Threading.Channels` | 6.0.0 | `src/Atomizer/Processing/QueuePump.cs` | Bounded producer/consumer channel |
| `Microsoft.EntityFrameworkCore` | 6.0.0 | `src/Atomizer.EntityFrameworkCore/Storage/EntityFrameworkCoreStorage.cs` | Relational storage backend |
| `Microsoft.EntityFrameworkCore.Relational` | 6.0.0 | Raw SQL execution in provider classes under `src/Atomizer.EntityFrameworkCore/Providers/Sql/` | Provider-specific SQL (`FOR UPDATE SKIP LOCKED`) |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | (consumer-supplied) | Detected via `ProviderName` in `RelationalProviderCache.cs` | PostgreSQL support |
| `Microsoft.EntityFrameworkCore.SqlServer` | (consumer-supplied) | Detected via `ProviderName` in `RelationalProviderCache.cs` | SQL Server support |
| `Pomelo.EntityFrameworkCore.MySql` / `MySql.EntityFrameworkCore` | (consumer-supplied) | Detected via `ProviderName` in `RelationalProviderCache.cs` | MySQL support |
| `Swashbuckle.AspNetCore` | 9.0.3 | `samples/Atomizer.Example`, `samples/Atomizer.EFCore.Example` | Swagger for samples only |
| `Testcontainers.*` | 4.6.0 | `tests/Atomizer.EntityFrameworkCore.Tests/Fixtures/` | Ephemeral DB containers for integration tests |

---

*Integration audit: 2026-05-03*
