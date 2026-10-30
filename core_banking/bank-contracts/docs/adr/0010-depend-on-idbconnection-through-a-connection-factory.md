# 0010. Depend on IDbConnection through a connection factory

- Status: Superseded by 0012 (EF Core; the provider is named only in AddNpgsqlDbContext), 2026-10-30
- Date: 2026-10-30
- Scope: bank-contracts (Bank.Messaging), used by every service
- Amends: 0009 (the types of `DbSession.Connection` and `DbSession.Transaction`, and where connections come from)
- Design plan reference: section 4, ADR row 01 (Dapper on Npgsql, unchanged)

## Context

The first version of `DbSession`, `OutboxDispatcher` and `SqlMigrationRunner` took an `NpgsqlDataSource` and exposed `NpgsqlConnection` and `NpgsqlTransaction`. That way the PostgreSQL driver leaked into every feature and handler that uses the session. Majid decided that only a factory may know the provider, and that everything else depends on `System.Data.IDbConnection`.

`IDbConnection` and `IDbTransaction` are older than async in .NET: they have only blocking `Open`, `BeginTransaction`, `Commit` and `Rollback`. The async versions exist only on the abstract classes `DbConnection` and `DbTransaction`. Majid asked whether async could still be kept.

## Decision

1. `IDbConnectionFactory.OpenAsync(ct)` returns an open `IDbConnection`. `NpgsqlConnectionFactory` implements it over an `NpgsqlDataSource`, which owns the connection pool. It is the only production type in the bank-contracts packages that names Npgsql. It is registered with `services.AddNpgsqlConnectionFactory(connectionString)`.
2. `DbSession` takes an `IDbConnectionFactory` and exposes `IDbConnection Connection` and `IDbTransaction Transaction`.
3. `DbAsyncExtensions` adds `BeginTransactionAsync`, `CommitAsync`, `RollbackAsync` and `DisposeAsyncIfPossible`:
   - if the object is a `DbConnection` or `DbTransaction`, they call its real async method;
   - only for a provider without them do they fall back to the blocking call.
   They never use `Task.Run`.
4. `OutboxDispatcher` and `SqlMigrationRunner` also take the factory. Each poll or run uses its own `new DbSession(factory)`, so all connection and transaction handling lives in one class.
5. Test code may still name Npgsql where the test is about PostgreSQL itself: `PostgresFixture` creates roles and schemas as the admin user, and tests assert on `PostgresException`.

## Rationale

- Features, handlers, the publisher, the dispatcher, the inbox and the runner all see only `System.Data` types. The provider is chosen in one place, at registration.
- Async is kept without blocking a thread. Every real ADO.NET provider's connection derives from `DbConnection`. At run time the `IDbConnection` is an `NpgsqlConnection`, so the extensions reach Npgsql's real async methods. Dapper's own async methods use the same check.
- Opening, the step with the most I/O, is done by the factory, which uses `OpenConnectionAsync` directly.
- `DbSession` now also serves the dispatcher and the runner, so begin, commit, rollback and dispose are written once.

## Alternatives considered

- `DbConnection` and `DbTransaction` as the public types: provider-neutral and async without extensions. Majid preferred the interface.
- `IDbConnection` with blocking calls only: rejected. Every request would block a thread on `Commit`.
- Wrapping blocking calls in `Task.Run`: rejected. It is "async over sync": the blocking only moves to another thread-pool thread, and under load it makes thread starvation worse.

## Consequences

- The SQL itself is still PostgreSQL (`FOR UPDATE SKIP LOCKED`, `ON CONFLICT`, `jsonb`, `pg_advisory_xact_lock`, `= ANY(@Ids)`). The factory hides the driver's types, not the database. Changing to another database would still mean rewriting the SQL.
- `DbSession` and the factory are in the namespace `Bank.Messaging.Data`, so features add `using Bank.Messaging.Data;`. Since ADR 0011 they are in the package and namespace `Bank.Toolkit.Data`.
- Tests prove that the extensions call the async methods when they exist and fall back otherwise (`DbAsyncExtensionsTests`).
- Code that needs a provider-specific feature (for example binary `COPY` for bulk import) must get it through a new factory method, not by casting the connection.
