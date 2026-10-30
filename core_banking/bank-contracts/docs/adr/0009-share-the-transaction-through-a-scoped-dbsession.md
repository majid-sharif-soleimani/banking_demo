# 0009. Share one connection and transaction through a scoped DbSession

- Status: Accepted, amended by 0010 (IDbConnection through a connection factory)
- Date: 2026-10-30
- Scope: bank-contracts (Bank.Messaging), used by every service
- Design plan reference: section 4, ADR row 07; `docs/implementation-decisions.md`, Database ("one NpgsqlConnection and one transaction per request or per handled message")

## Context

The outbox only works if the event row is written in the same transaction as the state change. The inbox only works if the inbox row, the handler's effect and the handler's outgoing events commit together. With EF Core the DbContext would carry the transaction. With Dapper, something has to hand the same `NpgsqlConnection` and `NpgsqlTransaction` to the feature's SQL, to `IEventPublisher` and to the inbox. Majid chose how in slice 1.2.

## Decision

1. `DbSession` is a scoped class in `Bank.Messaging` (namespace `Bank.Messaging.Data` since ADR 0010) with `OpenAsync`, `BeginAsync`, `CommitAsync` and `RollbackAsync`, and the properties `Connection` and `Transaction` (`IDbConnection` and `IDbTransaction` since ADR 0010). Disposing it without a commit rolls back.
2. Features and handlers inject `DbSession` and pass `session.Transaction` to every Dapper call that changes data. `IEventPublisher` keeps the signature from `docs/stages/stage-1.md` and writes to the outbox through the same session.
3. `PublishAsync` without an active transaction throws `InvalidOperationException`, so an event can never be written outside the state change's transaction.
4. For a handled message, `MessagePipeline` creates the scope, begins the transaction, writes the inbox row, calls the handler and commits. For an HTTP request, the feature begins and commits itself.
5. `DbSession` is a concrete class, not an interface: there is one implementation, and tests use a real PostgreSQL.

## Rationale

- The public interfaces stay free of Npgsql types and keep their fixed signatures. Since ADR 0010 `DbSession` is free of them too.
- A scope already exists per HTTP request, and the pipeline creates one per handler, so "one transaction per unit of work" falls out of normal dependency injection.
- Begin and commit stay visible in the feature code, matching the explicit style of ADR 0001.

## Alternatives considered

- Pass the transaction explicitly (`PublishAsync(event, key, transaction, ct)`): rejected. It is the most explicit, but it puts Npgsql into the public interface and changes its fixed signature. Every handler would also have to pass the transaction on.
- Ambient `TransactionScope`: rejected. It works implicitly, needs `TransactionScopeAsyncFlowOption.Enabled` everywhere, and can escalate to a distributed transaction by accident.

## Consequences

- The dependency is hidden: a feature that forgets `BeginAsync` fails at `PublishAsync`, not at compile time. The error message names the fix.
- A feature must not open its own second connection for writes; the review checklist should catch it.
- Read-only features can use `OpenAsync` without a transaction.
