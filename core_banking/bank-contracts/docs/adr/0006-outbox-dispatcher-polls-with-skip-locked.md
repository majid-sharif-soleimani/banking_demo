# 0006. Find new outbox rows by polling with FOR UPDATE SKIP LOCKED

- Status: Accepted
- Date: 2026-10-30
- Scope: bank-contracts (Bank.Messaging), used by every service
- Design plan reference: section 4, ADR row 07; `docs/implementation-decisions.md`, Messaging details

## Context

A service writes its events to `outbox_message` in the same transaction as its state change (design plan ADR 07). Something must then find the committed rows and send them to Kafka, at least once, even after a crash, and without two instances of the service sending the same rows at the same time. Majid reconfirmed the earlier decision in slice 1.2.

## Decision

1. `OutboxDispatcher.DispatchBatchAsync` is one poll: in one transaction it selects up to `BatchSize` (default 100) rows `WHERE sent_at IS NULL ORDER BY position ... FOR UPDATE SKIP LOCKED`, sends them in `position` order through `IMessageTransport`, sets `sent_at` on the ones that were sent, and commits.
2. A hosted `OutboxDispatcherService` calls it in a loop: again at once after a full batch, otherwise after `PollingInterval` (default 200 ms, configurable).
3. On the first failed send the poll stops, records `attempts` and `last_error` on that row, and commits what was sent before it. The row is retried on the next poll.
4. `position` is a `bigint` identity column. Rows are never deleted in phase 1; cleaning up sent rows is a later decision.

## Rationale

- One SQL statement covers finding, ordering and locking. It needs nothing beyond PostgreSQL, so it works the same in tests, locally and on AWS.
- `SKIP LOCKED` lets several instances poll at the same time without waiting for each other and without taking the same rows (the test `Concurrent_dispatchers_never_send_the_same_row_twice`).
- Stopping at the first failure keeps the send order per key within one dispatcher.
- A crash after the send and before the commit only causes a resend with the same message id, which the consumer's inbox absorbs (ADR 0007).

## Alternatives considered

- PostgreSQL LISTEN/NOTIFY: rejected. It has lower latency, but a notification is lost when no listener is connected, so polling is still needed as a fallback. That means two code paths for a gain of at most 200 ms.
- Debezium CDC (reading the WAL into Kafka): rejected. No dispatcher code, but it needs Kafka Connect, logical replication and more infrastructure to run and monitor. Too much for a demo with one developer.

## Consequences

- An idle service still runs a small indexed query every 200 ms (`ix_outbox_message_unsent` is a partial index on unsent rows).
- An event reaches Kafka up to `PollingInterval` after its commit.
- With more than one dispatcher instance, two rows with the same key can be sent out of order: instance A locks the first rows and instance B takes the next ones. For strict per-key order, run one dispatcher per service. That is the case in phase 1.
- One message that always fails to send blocks the whole outbox of that service. This is visible in `last_error` and in the logs. Acceptable for the demo.
- `outbox_message` grows without limit until a cleanup job exists.
