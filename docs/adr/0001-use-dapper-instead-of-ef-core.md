# 0001. Use Dapper instead of EF Core for data access

- Status: Superseded by 0002 (Use EF Core for data access), 2026-10-30
- Date: 2026-10-30
- Scope: workspace
- Design plan reference: section 4, ADR row 01

## Context

The design plan chose EF Core for every service. The core of this system is SQL-shaped work: writing the outbox row in the same transaction as the state change, claiming outbox rows with `FOR UPDATE SKIP LOCKED`, checking the inbox before a handler runs, unique business keys for financial effects, and optimistic concurrency. EF Core hides the generated SQL and the transaction boundary behind change tracking and `SaveChanges`, which makes these mechanics harder to see and to learn. No EF Core code had been written yet when this was decided (slice 1.1 only built `Bank.Contracts`).

## Decision

All services and shared packages use Dapper with hand-written SQL on Npgsql. No project references EF Core.

- One `NpgsqlConnection` and one transaction per request or per handled message; feature code, the outbox and the inbox use that same transaction.
- Schema changes are versioned SQL scripts per service.
- Table and column names are snake_case in SQL; Dapper maps them with `DefaultTypeMap.MatchNamesWithUnderscores = true`.
- Optimistic concurrency uses PostgreSQL `xmin`: updates add `AND xmin = @Version` and treat 0 affected rows as a conflict.

## Rationale

Every statement that touches money or messaging is visible in the code and can be read, reviewed and tested as SQL. The transaction boundary is explicit, which is exactly what the transactional outbox and the idempotent inbox depend on. Dapper is small, fast and has no change tracker to reason about. The feature code stays a vertical slice: SQL lives next to the endpoint or handler that uses it.

## Alternatives considered

- EF Core (the original plan): less SQL to write and built-in migrations, but the SQL and the transaction boundary are implicit, and outbox/inbox code ends up fighting change tracking.
- Raw ADO.NET with Npgsql only: fully explicit, but the hand-written row mapping adds noise without teaching anything new.
- EF Core for writes and Dapper for reads: two data-access styles in every service, more to learn and to keep consistent.

## Consequences

- Easier: seeing and testing the exact SQL, controlling transactions, using PostgreSQL features (`SKIP LOCKED`, `ON CONFLICT DO NOTHING` for the inbox, `xmin`).
- Harder: no migrations or change tracking for free. Every insert and update is written by hand, and a renamed column is not caught by the compiler, so integration tests against a real PostgreSQL (Testcontainers) carry more weight.
- Open D1 decisions for slice 1.2:
  - The SQL migration runner: DbUp, FluentMigrator, or a small hand-written runner.
  - How `IEventPublisher` joins the feature's connection and transaction (for example a scoped connection-and-transaction holder, or passing the transaction explicitly).
