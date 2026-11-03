# Slice 4: Search transactions across accounts

This document has three parts. Part 1 is the big picture: investigating suspicious movements needs one search over every account's entries. Part 2 covers the building blocks: the filter rules, the endpoint and its join, and the one index. Part 3 explains why, including bank-accounts ADR 0004. Tests, limits and questions come after.

---

## Part 1. The big picture

### 1.1 The goal

Until now, entries could only be read one account at a time (`GET /accounts/{id}/statement`). An investigator, or later the agent, asks other questions: "every movement over 10,000 SEK last week", "all transfers of this customer", "cash withdrawals in EUR this month". So Accounts needs one search over all entries, with filters, one page at a time.

### 1.2 Why it is hard

- **The table is the biggest in the bank and only grows.** A search must not scan it on every page, but every index slows down every posting.
- **Money has a currency.** "Over 10,000" means nothing without one, and 10,000 SEK must not match 10,000 EUR.
- **Withdrawals are negative.** A large withdrawal is −15,000. An investigator asking for "movements over 10,000" means size, not sign.
- **The search reaches every customer.** It is staff only, and the filters (customer, account) point at people.

---

## Part 2. The building blocks and how they connect

```mermaid
sequenceDiagram
    participant S as Staff or agent
    participant G as Gateway
    participant E as SearchTransactions endpoint
    participant L as Logic: TransactionSearch
    participant DB as account_entry ⋈ account
    S->>G: POST /api/accounts/transactions/search {from, to, minAmount:{10000,SEK}, kinds:[...]}  (X-Performed-By)
    G->>E: POST /transactions/search
    E->>E: StaffAccess.Problem, Paging.LimitProblem
    E->>L: Validate(filter, today) → 400 with a code; Currency(filter)
    E->>E: parse kinds (400 invalid_kind), decode cursor
    E->>DB: JOIN account; WHERE business_date BETWEEN ... AND currency = 'SEK' AND abs(amount) >= 10000 ... AND (business_date, sequence) > cursor ORDER BY business_date, sequence LIMIT 51
    DB-->>E: rows (range scan on ix_account_entry_business_date_sequence)
    E-->>S: { items: [TransactionItem...], nextCursor }
```

### 2.1 `Logic/TransactionSearch.cs` (pure)

`core_banking/bank-accounts/src/Bank.Accounts.Api/Logic/TransactionSearch.cs`, written test first. `TransactionFilter` holds the value filters: dates, amounts, currency. `Validate(filter, today)` returns the first problem as an `Error` with a code:

- `invalid_period`: `from` is after `to`;
- `date_in_future`: a date after today's business date (today is passed in, never read from a clock);
- `invalid_amount_range`: a negative size, or a minimum above the maximum;
- `currency_mismatch`: the amounts and `currency` name different currencies.

`Currency(filter)` returns the currency to search in: the filter's own, or else the amounts'.

### 2.2 The endpoint `POST /transactions/search`

`core_banking/bank-accounts/src/Bank.Accounts.Api/Features/Movements/SearchTransactions.cs`. Entries have no currency or customer, so the query joins `account` on its primary key and projects to an anonymous type `{ Entry, Number, CustomerId, Currency }`. Each filter adds a `Where`:

- dates: `business_date >= from`, `business_date <= to`;
- currency: `account.currency`;
- amounts: `Math.Abs(Amount)`, which becomes `abs(amount)`;
- kinds: `kind IN (...)`, case-insensitive names, 400 `invalid_kind` otherwise;
- customer: `account.customer_id`;
- account: `account_id`.

The keyset is `(business_date, sequence)`. Each `TransactionItem` gives the entry with its account number, owner, kind, signed amount and balance after it (as Money in the account's currency), the description and the reference.

### 2.3 The index `ix_account_entry_business_date_sequence`

`Data/AccountsDbContext.cs` and the migration `AddEntryBusinessDateIndex`: `(business_date, sequence)`. A date-range search reads this index from `from` onwards, already in page order, and stops after 51 matches. Account and customer searches use the indexes that were already there (`(account_id, sequence)` and `account (customer_id)`).

### 2.4 `Paging` and `StaffAccess` (copies)

`Features/Paging.cs` is the contract of ADR 0004. `StaffAccess` was added to `Features/Problems.cs` (ADR 0006).

---

## Part 3. Why it is designed this way

### 3.1 Decisions (D1, decided by Claude on Majid's delegation)

- **bank-accounts ADR 0004: one index, `(business_date, sequence)`.** It serves the main investigation query (a period) and the sort. An amount index would not help a search on `abs(amount)` with wide ranges. Indexes on kind or currency would cost a write per posting for filters that are always used with a date or an account.
- **Amounts are sizes in one currency.** The amount range compares `abs(amount)`, and its currency is the search currency.
- Workspace ADRs 0004, 0005 and 0006 apply as decided in slice 2.

### 3.2 Claude's choices (D2)

- **No required filter.** A search with no filters lists every entry, oldest first. It is correct, only slower without a date (ADR 0004, consequences).
- **Unknown account or customer** gives an empty page, not 404. It is a search, not a lookup.
- **Codes, not a validation dictionary**, for the 400s, as Accounts' other endpoints do (`Problems.Of`).

The design changed twice while building, and both changes came from the tests:

- The first version projected the join into a positional record, `Row(...)`. EF could not translate the `Where` clauses added after it, and the endpoint returned 500. An anonymous type fixed it.
- The first tests expected a transfer's debit before its credit. The two legs of one transfer are inserted in no fixed order, and the order flipped between runs. The tests now compare those two entries without order. The search itself is right: it sorts by `sequence`.

### 3.3 Patterns

- **Index for the access path, not for every column**: one index per real query shape.
- **Keyset on a natural order**: `(business_date, sequence)` is both the investigation order and the cursor.
- **Pure validation with the clock passed in** (`today`), so it is testable without time.

---

## What was tested

- `tests/Bank.Accounts.UnitTests/TransactionSearchTests.cs`: valid filters, period, future dates, negative and inverted ranges, three currency mismatches, and how the currency is chosen.
- `tests/Bank.Accounts.IntegrationTests/TransactionSearchTests.cs` (Testcontainers PostgreSQL), with two customers, SEK and EUR accounts and six entries over two business dates:
  - two pages in date and sequence order, with account, currency, owner and balance after;
  - an entry posted while paging comes at the end, and a page as long as the rest is the last;
  - filters by date, by amount size (the −15,000 withdrawal matches "≥ 10,000"), by range, by currency, and by an EUR amount;
  - filters by kinds, customer, account and combined, and an unknown account;
  - a customer (403) and a caller without an actor (401) refused;
  - seven bad inputs, each giving 400 with its code.

The integration tests were run three times in a row, so the fix for the transfer order is shown to hold.

```text
$ cd core_banking/bank-accounts && dotnet test
Passed!  - Failed: 0, Passed: 63, Skipped: 0, Total: 63 - Bank.Accounts.UnitTests.dll
Passed!  - Failed: 0, Passed: 74, Skipped: 0, Total: 74 - Bank.Accounts.IntegrationTests.dll
```

## What to watch later

- Index use was not checked with `EXPLAIN` on realistic volumes. The test tables are too small for PostgreSQL to choose an index. Check it on the home bank's data before relying on the speed.
- A search without a period reads the whole table.
- The investigation itself (patterns, thresholds, alerts) is phase 2 work for the agent. Any threshold it uses must come from configuration, not code.

## Check your understanding

1. Why does the amount filter use `abs(amount)`, and what would "minAmount 10,000" miss without it?
2. Which index serves `{ customerId, kinds: ["Withdrawal"] }`, and which serves `{ from, to, minAmount }`?
3. Why is a fourth index on `kind` a bad trade for this table?
