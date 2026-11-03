# 0004. Index the transaction search by business date only

- Status: Accepted
- Date: 2026-11-03
- Scope: bank-accounts
- Design plan reference: none (phase 1 extension for the phase 2 agent); workspace ADRs 0004–0006
- Decided by: Claude on Majid's delegation of 2026-11-03.

## Context

`POST /transactions/search` lists account entries across all accounts. It is the base for investigating suspicious movements. Its filters are a business date range, an amount range, a currency, movement types, a customer and an account, and it pages oldest first by `(business_date, sequence)` (workspace ADR 0004). `account_entry` is the largest table in the bank and only grows: every deposit, withdrawal, transfer leg, loan disbursement and repayment adds a row. Each index makes every one of those inserts slower and the table larger, so each index must earn its place. Entries carry no currency or customer; both live on `account` (indexed by `customer_id`), and entries are already indexed by `(account_id, sequence)`.

## Decision

Add one index: `ix_account_entry_business_date_sequence` on `account_entry (business_date, sequence)`.

How each filter is served:

| Filter | Served by |
| --- | --- |
| Date range, and the sort for every search | the new `(business_date, sequence)` index: a range scan that is already in page order |
| Account | the existing `(account_id, sequence)` index |
| Customer | the existing `account (customer_id)` index, then the account index |
| Currency | a filter on the joined `account` row |
| Amount range (size, `abs(amount)`) | a filter on rows found by the paths above |
| Movement types | a filter on rows found by the paths above |

Search rules:

- The amount range compares the movement's size, `abs(amount)`. It applies to one currency: the amounts' currency must equal `currency` when both are given (400 `currency_mismatch`), and it then sets the currency filter.
- Dates are business dates, inclusive, never after today's (400 `date_in_future`).
- These rules are checked in `Logic/TransactionSearch.cs`.

## Rationale

- An investigation almost always starts from a period ("last week", "this month"). The date index turns that into a range read in exactly the order the pages need, so a page costs about `limit` rows plus what the other filters skip.
- Account and customer searches already have indexes.
- An index on `amount` would not help much: a search by size needs `abs(amount)`, and amount ranges are wide ("over 10,000"). PostgreSQL would still visit most of the rows in the date range.
- One index adds one write per entry. Several indexes (amount, kind, currency) would add several, on the hottest insert path, for filters that are only ever used together with a date or an account.

## Alternatives considered

- No new index: the cheapest. But a date-range search, the main investigation query, would scan the whole table on every page.
- `(business_date, sequence)` plus an expression index on `abs(amount)`: faster "all large movements ever" searches. Rejected until such a search without a date proves common.
- A composite `(account_id, business_date)`: a little faster for "this account last month". The existing `(account_id, sequence)` index already narrows to one account, which has few entries.
- Denormalizing `currency` and `customer_id` onto `account_entry`: faster filters without a join, but two more columns to keep correct on every insert. The join on the account's primary key is cheap.

## Consequences

- Every entry insert also updates one more B-tree.
- A search with only an amount or a movement type, and no dates, account or customer, reads every entry. It is still correct, and it pages from the start of the index, but it is slower on a large table. The agent should be told to search with a period.
- The index is built by a normal (not `CONCURRENTLY`) migration. That blocks writes to `account_entry` while it builds. On the home bank's synthetic data this is expected to take well under a second, but it was not measured. On a large production table, it should be built `CONCURRENTLY` outside the migration.
- Within one business date, entries sort by `sequence`, the insert order. The two legs of one transfer are inserted in no fixed order.
