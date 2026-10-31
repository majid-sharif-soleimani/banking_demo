# 0002. Store the balance on the account, guarded by its row version

- Status: Accepted (to be reviewed by Majid)
- Date: 2026-10-31
- Scope: bank-accounts
- Design plan reference: section 2 (no overdraft; statements from the transaction table with balance after each entry); section 5 (Account.Balance, AccountEntry.BalanceAfter)
- Decided by: Claude on Majid's delegation of 2026-10-31. A D1 for Majid in the stage 4 file.

## Context

Every withdrawal must check "is there enough money?", and two withdrawals at the same moment must not both pass the check. The balance can either be stored on the account and changed with every entry, or always computed as the sum of the entries.

## Decision

- `account.balance` is stored. Every movement goes through `Postings.Post`, which changes the balance and appends an `account_entry` with `balance_after`, in one `SaveChangesAsync`.
- The account's `xmin` (Version) is in the update's `WHERE`. A concurrent change makes the second save fail with `DbUpdateConcurrencyException`, the command runs once more on fresh data, and a second conflict answers 409 (stage file, "Concurrency").
- Entries are append-only, and a check constraint `balance >= 0` is the last line of defence.
- Amounts are stored in unconstrained `numeric` with exactly the currency's minor units (`AmountScale`).

## Rationale

- The overdraft check reads one row and the protection is the row version the workspace already uses everywhere. No locks and no aggregate query on a hot path.
- `balance_after` makes statements and balance-at-date a simple read of entries (slice 4.4), as the design plan asks.
- The check constraint means even a bug cannot store a negative balance.

## Alternatives considered

- **Always compute from entries** (`SUM(amount)`): one source of truth with nothing to keep in sync. But each withdrawal needs the sum and a way to stop two concurrent withdrawals, either `SELECT … FOR UPDATE` on the account or SERIALIZABLE isolation, and the sum grows with the history. Rejected for the hot path. The entries still allow a reconciliation check (stage 7 compares each balance with its statement sum).
- **Pessimistic lock (`FOR UPDATE`) with a stored balance:** correct with no retries, but raw SQL in every movement. Optimistic concurrency is already the workspace rule.

## Consequences

- The balance and the entries must never be changed separately; `Postings.Post` is the only writer.
- Under heavy contention on one account, some requests get 409 after one retry. A caller retries with the same Idempotency-Key.
