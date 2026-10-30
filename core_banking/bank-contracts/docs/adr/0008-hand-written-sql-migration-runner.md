# 0008. Apply SQL migrations with a small hand-written runner

- Status: Superseded by 0012 (EF Core migrations), 2026-10-30
- Date: 2026-10-30
- Scope: bank-contracts (Bank.Messaging), used by every service
- Design plan reference: section 4, ADR row 01 (Dapper and hand-written SQL); workspace ADR 0001; `docs/implementation-decisions.md`, Database

## Context

With Dapper instead of EF Core (workspace ADR 0001), there are no EF migrations. Each service still needs versioned SQL scripts for its schema, applied at startup in development and by the AppHost. `Bank.Messaging` itself ships the outbox and inbox tables. Majid chose the runner in slice 1.2.

## Decision

1. `SqlMigrationRunner.RunAsync(scripts)` in `Bank.Messaging` (in `Bank.Toolkit.Data` since ADR 0011):
   - runs in one transaction on the service's own connection (the schema comes from the role's search path)
   - takes `pg_advisory_xact_lock(hashtext(current_schema()))`, so only one instance of a service migrates at a time
   - creates `schema_migration (script_id, checksum, performed_by, created_at)` if it does not exist
   - applies each script that is not in it, in the given order, and records it
2. An applied script whose SHA-256 checksum (with normalized line endings) has changed stops the run with an error. Applied scripts are never edited; a change is a new script.
3. Scripts are `.sql` files embedded with `LogicalName="migrations/%(Filename)%(Extension)"` and ordered by name (`0001_...`). The script id is `<source>/<file name>`: `messaging/...` for the outbox and inbox, `<service>/...` for the service's own.
4. `services.AddSqlMigrations(assembly, serviceName)` runs the messaging scripts and then the service's scripts in `IHostedLifecycleService.StartingAsync`, before any hosted service (such as the outbox dispatcher) starts. Since ADR 0011, `AddSqlMigrations(assembly, source)` is called once per assembly with scripts: `AddBankMessaging` registers the messaging scripts and the service registers its own. All sources run in registration order.

## Rationale

- About a hundred lines that Majid can read in full. They show the three things every migration tool must solve: a journal, a lock against concurrent runs, and detection of edited scripts.
- One transaction per run uses PostgreSQL's transactional DDL: a failing script leaves the schema exactly as it was (test `Failing_script_rolls_back_the_whole_run`).
- The SQL stays plain SQL, which is the point of ADR 0001.

## Alternatives considered

- DbUp: a good fit with plain `.sql` files and a journal table. Rejected by Majid in favour of seeing and owning the mechanism. It would be the first choice if the runner ever needs features it lacks.
- FluentMigrator: rejected. Migrations in a C# DSL hide the SQL, which goes against ADR 0001, and down migrations are not needed.

## Consequences

- We maintain the runner and its tests (`MigrationRunnerTests`: applied once, edited script detected, failing script rolled back, concurrent runs).
- No down migrations. A wrong migration is fixed with a new forward script.
- Statements that cannot run in a transaction (`CREATE INDEX CONCURRENTLY`, `ALTER TYPE ... ADD VALUE` before PostgreSQL 12) are not supported. If one is ever needed, the runner gets a per-script opt-out.
- All pending scripts run in one transaction, so a large data migration holds locks until the end. Acceptable at this size.
