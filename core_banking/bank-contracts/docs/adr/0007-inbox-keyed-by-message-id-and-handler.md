# 0007. Key the inbox on (message id, handler name)

- Status: Accepted (since 0012 implemented on EF Core with `ExecuteSqlAsync`; behaviour unchanged)
- Date: 2026-10-30
- Scope: bank-contracts (Bank.Messaging), used by every service
- Design plan reference: section 4, ADR rows 07 and 08; `docs/implementation-decisions.md`, Messaging details

## Context

Delivery is at least once: the outbox can send a message twice (ADR 0006), and Kafka redelivers when an offset was not committed. Each handler must apply a message once in effect. One service can have more than one handler for the same event, for example Lending reacting to `DayClosed` both for interest accrual and for overdue checks. Majid reconfirmed the earlier decision in slice 1.2.

## Decision

1. `inbox_message` has the primary key `(message_id, handler_name)`. The message id is the event's `EventId`, and the handler name is the handler's class name.
2. For each handler of a message, `MessagePipeline` opens its own scope and transaction, inserts the inbox row with `ON CONFLICT DO NOTHING`, and:
   - if no row was inserted, rolls back and skips the handler (`Duplicate`)
   - otherwise calls the handler and commits the inbox row, the handler's effect and any outgoing outbox rows together
3. Financial effects also have a unique business key in the handler's own table, such as `(loan_id, installment_no)` (design plan ADR 08). The inbox catches redeliveries; the business key catches the same fact under a new message id.

## Rationale

- When one handler has committed and the next one fails, the transport redelivers the whole message. With the handler in the key, the committed handler is skipped and only the failed one runs again (test `Each_handler_applies_a_message_once_even_when_another_handler_failed`).
- One transaction per handler keeps handlers independent: one failing handler does not roll back another's committed work.
- `ON CONFLICT DO NOTHING` on the primary key also makes concurrent deliveries safe: the second insert waits for the first transaction and then sees the row.

## Alternatives considered

- Message id only: rejected. Two handlers of one event would need one shared transaction, so one failing handler rolls back the others. Otherwise the second handler sees the message as a duplicate and never runs.

## Consequences

- The handler class name is part of stored data. Renaming a handler makes it see old messages as new. This is harmless while those messages are no longer redelivered, but a rename should be a deliberate change. Two handlers with the same class name in one service fail at startup.
- One inbox row per handler and message. Old rows can be deleted after the topic's retention period; there is no cleanup job yet.
