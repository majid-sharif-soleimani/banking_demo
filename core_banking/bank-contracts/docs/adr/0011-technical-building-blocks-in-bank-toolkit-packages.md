# 0011. Keep technical building blocks in Bank.Toolkit packages, apart from Bank.Common

- Status: Accepted, amended by 0012 (`Bank.Toolkit.Data` now holds the EF Core registrations)
- Date: 2026-10-30
- Scope: bank-contracts (package layout), used by every service
- Amends: 0001 (adds the package `Bank.Toolkit.Data`), 0008 (where the runner lives and how scripts are registered), 0010 (where the factory and `DbSession` live)
- Design plan reference: section 4, ADR row 17; `docs/implementation-decisions.md`, "Shared code"

> Since ADR 0012, `Bank.Toolkit.Data` holds the EF Core registrations (`AddNpgsqlDbContext`, `AddMigrationsOnStartup`) instead of the Dapper-based types listed in point 3. The rules in points 1 and 2 are unchanged.

## Context

After ADR 0010, the connection factory, `DbSession`, the async helpers and the migration runner lived in `Bank.Messaging` (namespace `Bank.Messaging.Data`). They are not about messaging. Every service's features use `DbSession` for their own SQL, and the runner creates every service's own tables. Majid also expects more shared technical code later, such as a special dictionary needed by two projects. He wants a clear home for it, and he does not want such code in `Bank.Common`.

## Decision

1. **Two kinds of shared code, two homes:**

   | Home | What belongs there | Examples |
   | --- | --- | --- |
   | `Bank.Common` (unchanged) | The shared business vocabulary of the bank: what the services' business code talks to directly | `IBusinessClock`, `MoneyRounding`, `Result<T>`, `PerformedBy`, the external-service interfaces |
   | `Bank.Toolkit.*` packages | Technical building blocks that have nothing to do with banking | database access now; a special collection later; any other shared technical tool |

2. **Rules for `Bank.Toolkit.*`:**
   - It references no `Bank.*` package: no `Money`, no events, no business rules. It could be used by any application.
   - Code goes there only when at least two real projects need it.
   - One package per technical area, named `Bank.Toolkit.<Area>`, so a project that needs one area does not get the dependencies of the others. A package is created only when its first real content exists; no empty packages ahead of time.
3. **The first package is `Bank.Toolkit.Data`**, namespace `Bank.Toolkit.Data` (and `Bank.Toolkit.Data.Migrations`). It holds:
   - `IDbConnectionFactory`, `NpgsqlConnectionFactory`, `DbSession`, `DbAsyncExtensions` (moved from `Bank.Messaging.Data`)
   - `MigrationScript`, `MigrationScripts`, `MigrationSource`, `SqlMigrationRunner`, `MigrationStartupService` (moved from `Bank.Messaging.Migrations`)
   - `AddNpgsqlConnectionFactory(connectionString)`, which registers the factory and a scoped `DbSession` and sets Dapper's snake-case mapping
   - `AddSqlMigrations(assembly, source)`, which can be called once per assembly that ships scripts

   Its dependencies are Npgsql, Dapper and `Microsoft.Extensions.Hosting.Abstractions`. `NpgsqlConnectionFactory` stays inside it (it is the only Npgsql type, ADR 0010).
4. **`Bank.Messaging` references `Bank.Toolkit.Data`** and no longer references Npgsql. It keeps the SQL of its own tables (`0001_outbox_and_inbox.sql`), and `AddBankMessaging` registers it with `AddSqlMigrations(..., "messaging")`. A service only registers its own scripts.
5. Tests move with the code: `tests/Bank.Toolkit.Data.UnitTests` (`DbAsyncExtensionsTests`) and `tests/Bank.Toolkit.Data.IntegrationTests` (`MigrationRunnerTests`, PostgreSQL in Docker).

## Rationale

- A name that says what is inside: `Bank.Common` is bank vocabulary, `Bank.Toolkit` is technique. "Is this about the bank?" decides where a new class goes.
- `Bank.Common` stays free of I/O and of external packages. The `Logic/` code of every service and its unit tests can reference it without getting a database driver.
- One package per area keeps dependencies narrow, as `Bank.Messaging` and `Bank.Messaging.Kafka` already do for the broker.
- `Bank.Messaging` is now free of provider types too. It depends only on `System.Data` through the toolkit.
- Registering migrations per source lets each package bring its own tables, so a service can no longer forget the outbox and inbox scripts.

## Alternatives considered

- `Bank.Data`: rejected by Majid as too narrow; it could only ever hold database code.
- `Bank.Framework`: rejected; too general, it would invite unrelated code and overlap with `Bank.Common`.
- `Bank.Infrastructure.*`: discussed. Usually it means adapters to external systems, which fits data but not a collection, and it suggests the layered architecture rejected in design plan ADR 02.
- Moving the code into `Bank.Common`: rejected; it would bring Npgsql and Dapper into the package that business logic depends on.
- Keeping it in `Bank.Messaging` with another namespace: rejected; a package called "messaging" would carry general database tools.

## Consequences

- Six packages instead of five, all still versioned together from `bank-contracts`.
- Features and handlers use `using Bank.Toolkit.Data;`. A service's `Program.cs` calls `AddNpgsqlConnectionFactory`, `AddBankMessaging`, and `AddSqlMigrations` for its own scripts.
- The rule "no `Bank.*` reference in `Bank.Toolkit.*`" can be checked by looking at the project files. A reviewer should reject a toolkit class that knows about money, events or business rules.
