# 0012. Build the outbox, the inbox and data access on EF Core

- Status: Accepted
- Date: 2026-10-30
- Scope: bank-contracts (Bank.Toolkit.Data, Bank.Messaging), used by every service
- Supersedes: 0008 (hand-written migration runner), 0009 (scoped DbSession), 0010 (IDbConnection through a connection factory)
- Amends: 0001 (package dependencies), 0011 (what `Bank.Toolkit.Data` holds)
- Design plan reference: section 4, ADR rows 01 and 07; workspace ADR 0002

## Context

Workspace ADR 0002 moved all data access from Dapper to EF Core. The shared packages had been built for Dapper, with `DbSession`, `IDbConnectionFactory`, `DbAsyncExtensions` and `SqlMigrationRunner` in `Bank.Toolkit.Data`. They need a new shape that keeps the guarantees of ADRs 0006 and 0007: the outbox row is saved with the state change, each poll locks rows with `SKIP LOCKED`, and the inbox row, the handler's effect and its outgoing events commit together.

## Decision

1. **The service's `DbContext` is the unit of work** and replaces `DbSession`. `Bank.Toolkit.Data` registers it with `AddNpgsqlDbContext<TContext>(connectionString)`:
   - it calls `UseNpgsql` with the snake_case naming convention (`UseNpgsqlWithBankConventions`, also used by design-time factories);
   - it registers the same scoped instance as `DbContext`, so shared code works on the service's context without knowing its type.

   It is the only code that names the database provider.
2. **Migrations are EF Core migrations.** `AddMigrationsOnStartup<TContext>()` runs `Database.MigrateAsync` in `IHostedLifecycleService.StartingAsync`, before any hosted service starts. EF Core's own migration lock makes concurrent starts safe (test `Instances_starting_together_apply_each_migration_once`).
3. **Messaging tables are part of the service's model.** `Bank.Messaging` provides the entities `OutboxMessage` and `InboxMessage` and `modelBuilder.AddBankMessaging()`, with explicit table, column, key and index names, because raw SQL refers to them. Each service calls it in `OnModelCreating`, and its own migrations create the tables in its schema.
4. **Publishing only tracks.** `OutboxEventPublisher` adds an `OutboxMessage` to the context. The feature's `SaveChangesAsync` writes it together with the state change, in one transaction, or not at all. There is no "publish without a transaction" error any more: an unsaved event is simply never written.
5. **The dispatcher** creates its own scope and context per poll, opens a transaction, and loads the batch with `FromSql($"... FOR UPDATE SKIP LOCKED")`. It sends the rows, marks them on the tracked entities, saves and commits. The behaviour of ADR 0006 is unchanged.
6. **The pipeline** gives each handler its own scope, context and transaction:
   - it inserts the inbox row with `ExecuteSqlAsync($"INSERT ... ON CONFLICT DO NOTHING")` before the handler runs;
   - it calls the handler, which only changes the context and publishes;
   - it calls `SaveChangesAsync` and commits.

   Handlers never call `SaveChangesAsync`. ADR 0007 is unchanged.
7. `Bank.Messaging` references `Microsoft.EntityFrameworkCore.Relational`, not the Npgsql provider. `Bank.Toolkit.Data` references the Npgsql provider and `EFCore.NamingConventions`. `dotnet-ef` is a local tool (`dotnet-tools.json`).

## Rationale

- One `SaveChangesAsync` for the state change and its outbox row is the simplest possible form of the transactional outbox. A feature cannot save one without the other.
- The two statements that LINQ cannot express stay raw SQL, so the mechanics of ADRs 0006 and 0007 are as visible as before.
- The inbox row is inserted *before* the handler runs, not added to the context. A duplicate is then detected without running the handler, and a concurrent delivery waits on the primary key.
- Registering the context also as `DbContext` keeps `Bank.Messaging` generic without a type parameter on every class.

## Alternatives considered

- A separate `MessagingDbContext` with its own migrations inside `Bank.Messaging`: one migration for all services, but the outbox row would live in another context than the state change. Every feature would then need an explicit shared transaction across two contexts, which loses the single `SaveChangesAsync`.
- Adding the inbox row to the context and catching the unique violation at `SaveChangesAsync`: rejected; the handler would run before the duplicate is detected, and its work would be thrown away or, for side effects outside the database, repeated.
- `AddBankMessaging<TContext>()` with the context type on every messaging class: rejected; more generic noise for the same result as the `DbContext` registration.

## Consequences

- A service's `OnModelCreating` must call `modelBuilder.AddBankMessaging()`. If it does not, the publisher, dispatcher and pipeline fail with a message that says so.
- A change to the messaging tables means a new migration in every service after it updates `Bank.Messaging`.
- `Bank.Toolkit.Data` now holds two small registrations. The `Bank.Toolkit` rule of ADR 0011 is unchanged.
- Tests that check a business-key violation see `DbUpdateException` with an inner `PostgresException`.
