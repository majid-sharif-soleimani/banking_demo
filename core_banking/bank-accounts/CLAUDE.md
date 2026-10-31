# bank-accounts

Accounts and money movements: accounts, deposits and withdrawals, transfers (with FX), statements, balance at a date, standing orders at end of day. Schema `accounts`, topic `bank.accounts.events`. End-of-day participant. Plan: `docs/stages/stage-4-plan.md`.

## Tables

- `account`: 9-digit `number` (unique, Luhn), stored `balance` (ADR 0002, check `balance >= 0`), `xmin` on every change.
- `account_entry`: append-only, signed `amount`, `balance_after`, `business_date`, `sequence` (identity, order).
- `idempotency_record`: (operation, key) → request hash + stored response as text (ADR 0003).
- `customer_ref`, `currency_ref` (local copies), `business_day` (consumer), outbox, inbox.

## Events

- Publishes `AccountOpened`, `MoneyDeposited`, `MoneyWithdrawn` (key = account id).
- Consumes `CustomerRegistered` (customers), `CurrencyActivated`, `BusinessDayOpened`, `DayClosed` (operations).

## Rules in code

- Every balance change goes through `Features/Movements/Postings.Post`; amounts are normalized to the currency's minor units (`Logic/AmountScale`). Columns are unconstrained `numeric`, which keeps the written scale.
- Every money POST runs through `IdempotentCommand.RunAsync`: Idempotency-Key required; one retry on an xmin conflict, then 409.
- A customer missing from `customer_ref` → 422 `customer_not_found` (ADR 0001); the caller retries.
- `X-Customer-Id` for another customer → 404.

## Gotchas

- Do not store responses as jsonb: it reorders keys and breaks byte-identical replays.
- Request amounts are checked (`Postings.CheckAmount`) before the balance check; the minor units come from `currency_ref`.
- Tests: `Support/AccountsApp` (on `BankServiceApp`) delivers the business date, SEK/EUR/USD and one customer at start.

## Commands

```bash
dotnet build && dotnet test                         # integration tests need Docker
dotnet tool restore && dotnet ef migrations add <Name> --project src/Bank.Accounts.Api --output-dir Data/Migrations
```
