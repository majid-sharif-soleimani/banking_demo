# 0001. Represent the schedule as immutable records

- Status: Accepted (to be reviewed by Majid)
- Date: 2026-10-31
- Scope: bank-lending
- Design plan reference: section 4, ADR row 02 (pure functions under `Logic/`)
- Decided by: Claude on Majid's delegation of 2026-10-31. A D1 for Majid in the stage 3 file.

## Context

Every loan calculation reads a schedule and most of them change it: allocation adds payments, penalties add penalty amounts, payoff reads what is left. The functions must be pure (financial-logic rule), and stage 5 will load the schedule from the database, call a function and save the result. The question is whether a function changes the items it gets or returns new ones.

## Decision

`ScheduleItem` is a positional `sealed record` with only `init` properties. Functions take `IReadOnlyList<ScheduleItem>` and return a new list with new items (`item with { PaidInterest = … }`). An item's status (`Unpaid`, `PartiallyPaid`, `Paid`) and its unpaid amounts are computed properties, never stored.

## Rationale

- A pure function that returns new values cannot surprise its caller: the schedule the caller still holds is exactly what it passed in, so "before" and "after" can be compared in tests and in stage 5 (to know which rows changed).
- Property-based tests can run one input through several functions without copying it first.
- A computed status can never disagree with the amounts.

## Alternatives considered

- **Mutable items changed in place:** no copies, and it maps straight onto EF entities. But a function's effect is hidden in its arguments, a failed half-way calculation leaves the input changed, and tests need defensive copies. Rejected.
- **Stored status field:** easier to query in SQL, but it can drift from the amounts. Stage 5 can still store a status column derived from the computed one.

## Consequences

- Each call allocates a new list (at most 360 small records). That is negligible.
- Stage 5 maps `ScheduleItem` to and from an entity. The entity can be mutable; `Logic/` never sees it.
