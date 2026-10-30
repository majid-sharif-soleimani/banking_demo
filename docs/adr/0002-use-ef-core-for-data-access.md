# 0002. Use EF Core for data access

- Status: Accepted
- Date: 2026-10-30
- Scope: workspace
- Supersedes: 0001 (Use Dapper instead of EF Core)
- Design plan reference: section 4, ADR row 01 (back to EF Core, as originally planned)

## Context

ADR 0001 replaced EF Core with Dapper earlier the same day, so that the outbox, the inbox and the transaction boundary stay visible as SQL. Slice 1.2 then built those mechanics on Dapper, plus what Dapper does not give: a scoped `DbSession` to share the transaction, a connection factory, async helpers for `IDbConnection`, and a hand-written migration runner. After seeing that cost, Majid decided to use EF Core after all.

## Decision

All services and shared packages use EF Core 10 with the Npgsql provider. Dapper is not used anywhere.

- **Unit of work.** Each service has one `DbContext`, registered as scoped. A request or a handled message uses one instance. `SaveChangesAsync` writes all tracked changes in one transaction, including outbox rows added by `IEventPublisher`. A feature that needs several saves opens `Database.BeginTransactionAsync` itself.
- **Migrations.** EF Core migrations per service, in the service's `Data/Migrations`, created with `dotnet ef migrations add`. They are applied at startup in development and by the AppHost, before any hosted service starts.
- **Names.** snake_case through `EFCore.NamingConventions`. Table names are set explicitly and singular (`ToTable("loan")`). The schema comes from the role's search path, so the model names no schema.
- **Optimistic concurrency.** A `uint Version` property mapped to PostgreSQL `xmin` with `.IsRowVersion()`. EF adds it to the `WHERE` of every update and throws `DbUpdateConcurrencyException` when no row matches.
- **Raw SQL where LINQ has no equivalent.** `FromSql` and `ExecuteSqlAsync` with interpolated parameters, for example `FOR UPDATE SKIP LOCKED` and `ON CONFLICT DO NOTHING`. Never string concatenation.

## Rationale

- EF Core gives the unit of work, migrations, async, mapping and concurrency out of the box. With Dapper, slice 1.2 needed about 250 lines of our own for the same things.
- The atomic "state change plus outbox row" becomes simpler: both are tracked by the same context and saved by one `SaveChangesAsync`, so a feature cannot write one without the other.
- The few places that need PostgreSQL-specific SQL still get it, through `FromSql` and `ExecuteSqlAsync`, and stay visible in the code.

## Alternatives considered

- Keep Dapper (ADR 0001): every statement is visible SQL, but the project would maintain its own unit of work, connection factory and migration runner.
- EF Core for features, Dapper for the outbox and inbox: rejected; two data-access styles and two ways of sharing one transaction.

## Consequences

- SQL is generated for normal reads and writes. To see it, enable EF logging, or run `dotnet ef migrations script --idempotent` for the schema.
- Change tracking decides what is written. A feature must call `SaveChangesAsync` exactly once at the end, or open a transaction when it needs several.
- Every service depends on EF Core and the Npgsql provider (through `Bank.Toolkit.Data`).
- Messaging tables become part of each service's model and migrations (bank-contracts ADR 0012). A change to them means a new migration in every service.
