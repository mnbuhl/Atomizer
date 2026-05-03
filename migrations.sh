#!/usr/bin/env bash
set -euo pipefail

NAME="${1:-Initial}"
PROJECT=samples/Atomizer.EFCore.Example/Atomizer.EFCore.Example.csproj

dotnet ef migrations add --project "$PROJECT" --startup-project "$PROJECT" --context Atomizer.EFCore.Example.Data.Postgres.ExamplePostgresContext --configuration Debug "$NAME" --output-dir Data/Postgres/Migrations
dotnet ef migrations add --project "$PROJECT" --startup-project "$PROJECT" --context Atomizer.EFCore.Example.Data.SqlServer.ExampleSqlServerContext --configuration Debug "$NAME" --output-dir Data/SqlServer/Migrations
dotnet ef migrations add --project "$PROJECT" --startup-project "$PROJECT" --context Atomizer.EFCore.Example.Data.Sqlite.ExampleSqliteContext --configuration Debug "$NAME" --output-dir Data/Sqlite/Migrations
dotnet ef migrations add --project "$PROJECT" --startup-project "$PROJECT" --context Atomizer.EFCore.Example.Data.MySql.ExampleMySqlContext --configuration Debug "$NAME" --output-dir Data/MySql/Migrations