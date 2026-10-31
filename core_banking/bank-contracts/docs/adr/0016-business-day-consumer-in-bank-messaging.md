# 0016. Put the business-day consumer in Bank.Messaging

- Status: Accepted (to be reviewed by Majid)
- Date: 2026-10-31
- Scope: bank-contracts
- Design plan reference: section 4, ADR row 14; `implementation-decisions.md`, "Time and the business date"
- Decided by: Claude on Majid's delegation of 2026-10-31. This was a D1 for Majid in the stage 2 file; he should review it.

## Context

Every service except Operations must know the bank's business date without reading the system clock or Operations' schema. `implementation-decisions.md` says each service keeps a local `business_day` table, updated from `BusinessDayOpened` and `DayClosed`, and that `IBusinessClock.Today` reads it. Accounts, Cards, Lending, Ledger and Customers all need exactly the same table, handlers and clock. Writing them five times would mean five copies of a subtle piece of code: idempotency, out-of-order dates, and one date per request. The stage file asked where this shared code lives: `Bank.Common` or `Bank.Messaging`.

## Decision

The code lives in `Bank.Messaging`, namespace `Bank.Messaging.BusinessDays`:

- `LocalBusinessDay`, a date (PK) and a status (`Open` or `Closed`, as text), plus `created_at`, `performed_by` and `xmin`. `modelBuilder.AddLocalBusinessDays()` maps it to `business_day` in the service's own schema, next to `AddBankMessaging()`, and the service's own migration creates it.
- `LocalBusinessDayOpenedHandler` inserts a date it does not know. `LocalDayClosedHandler` marks the date Closed, and inserts it as Closed if it was never seen. Both are idempotent, and their class names are distinct from the service's own handlers, because handler names are inbox keys.
- `DatabaseBusinessClock : IBusinessClock`, scoped. `Today` is the **latest** date in the table, read once per scope.
- `services.AddBusinessDayConsumer()` registers the two handlers and the clock. The service lists `"operations"` in `Kafka:SubscribeTo`.

`Bank.Messaging` now references `Bank.Common`, for `IBusinessClock`. The packages move to `0.2.0`.

## Rationale

- **`Bank.Common` is ruled out by its own rule**: no I/O and no external packages (ADR 0011). The consumer needs EF Core and the handler interface.
- **`Bank.Messaging` already holds everything the consumer is made of**: `IEventHandler<T>`, the inbox pipeline, and "your model gets these tables from an extension method". Every service already references it. The consumer is the receiving side of a message flow, written once.
- **"Latest date wins"** makes the handlers safe against duplicates and against `BusinessDayOpened(D+1)` overtaking `DayClosed(D)`. Day events are keyed by date, so different dates can sit on different partitions.
- **One date per scope**: a request or a handled message that started on day D finishes on day D, even if D+1 opens meanwhile.

## Alternatives considered

- **`Bank.Common`**: it is where `IBusinessClock` lives, but it would have to take EF Core and messaging dependencies, which breaks ADR 0011. Rejected.
- **A new package, for example `Bank.BusinessDays`**: the cleanest boundary, but one more package to pack, version and reference, for about 150 lines that always come with `Bank.Messaging` anyway. Rejected for now; it can be split out if the code grows.
- **Each service writes its own copy**: no shared-code change, but five copies of subtle logic. Rejected.
- **A singleton clock cached in memory and updated by the handler**: no database read per scope, but the cache would change before the handler's transaction commits (or even if it rolls back), and several instances would disagree. Rejected.

## Consequences

- `Bank.Messaging` now carries one piece of business vocabulary (the business date). Keep it to that. Anything bigger belongs in its own package.
- `Today` is a synchronous property, so the first access in a scope runs one small synchronous query. That is acceptable for one indexed `max(date)`.
- `Today` counts a Closed day too: between `DayClosed(D)` and `BusinessDayOpened(D+1)` the business date is still D, because the end-of-day work for D runs then. Lending (stage 5) relies on this.
- Until the first `BusinessDayOpened` arrives, `Today` throws, with a hint to check `Kafka:SubscribeTo`. A service started without Operations fails loudly, not with a wrong date.
- Operations does not use this. It owns the date and reads its own `business_day` table (`BusinessDayQueries` in bank-operations).
- Tests use `FakeBusinessClock` from `Bank.Testing` for logic, and the real consumer in integration tests.
