# bank-accounts

Accounts and money movements: accounts, deposits and withdrawals, transfers (with FX), statements, balance at a date, standing orders at end of day. Schema `accounts`, topic `bank.accounts.events`. End-of-day participant. Plan: `docs/stages/stage-4-plan.md`.

## Tables

- `account`: 9-digit `number` (unique, Luhn), stored `balance` (ADR 0002, check `balance >= 0`), `xmin` on every change.
- `account_entry`: append-only, signed `amount`, `balance_after`, `business_date`, `sequence` (identity, order).
- `transfer`: amount and currency, converted amount and target currency, `fx_rate`, `fx_source`, `fx_observed_on`, `fx_at`, optional `standing_order_id`.
- `standing_order` (next run date, status Active/Cancelled/Completed), `standing_order_run` (unique (order, run date), Succeeded/Failed + reason), `end_of_day_run` (one row per business date Accounts completed).
- `idempotency_record`: (operation, key) → request hash + stored response as text (ADR 0003).
- `customer_ref`, `currency_ref` (local copies), `business_day` (consumer), outbox, inbox.

## Events

- Publishes `AccountOpened`, `MoneyDeposited`, `MoneyWithdrawn` (key = account id), `MoneyTransferred` (key = transfer id; `FxRate` as a decimal string, `Fee` always null), `StandingOrderFailed` (key = order id), `DayCompleted(D, "accounts")`.
- Consumes `CustomerRegistered` (customers), `CurrencyActivated`, `BusinessDayOpened`, `DayClosed` (operations).

## Rules in code

- Every balance change goes through `Features/Movements/Postings.Post`; amounts are normalized to the currency's minor units (`Logic/AmountScale`). Columns are unconstrained `numeric`, which keeps the written scale.
- Every money POST runs through `IdempotentCommand.RunAsync`: Idempotency-Key required; one retry on an xmin conflict, then 409.
- A customer missing from `customer_ref` → 422 `customer_not_found` (ADR 0001); the caller retries.
- `X-Customer-Id` for another customer → 404.

## End of day

- `AccountsEndOfDayHandler` on `DayClosed(D)`: every active order with next run ≤ D, each in its own scope and transaction (one rerun on an xmin conflict); failures are recorded and published, never retried; next run date advances either way; then `end_of_day_run` + `DayCompleted` with the inbox row. A second `DayClosed(D)` (new id) is a no-op.
- Its class name is an inbox key and must differ from the consumer's `LocalDayClosedHandler`.
- `Logic/StandingOrders.NextRunDate` counts every run from the start date (31st → last day of shorter months, then back to the 31st).

## Exchange rates

- `ExchangeRates:Provider` = `Fixed` (default; `ExchangeRates:SekPerUnit:EUR` etc.) or `Riksbank` (SWEA `Observations/Latest/SEK{CUR}PMI`; the response shape `{date, value}` is assumed from the public docs; check it against the live API before relying on it). No rate → 422 `fx_unavailable`.
- Rates are SEK per unit; inverted and crossed rates keep 10 decimals (`Logic/Fx`). Converted amounts round AwayFromZero to the target's minor units.

## Gotchas

- Opposite transfers can deadlock (40P01); `IdempotentCommand` treats it like a version conflict: one rerun, then 409.
- `TransferMoney.ExecuteAsync` is shared by `POST /transfers` and standing orders; it never saves.

- Do not store responses as jsonb: it reorders keys and breaks byte-identical replays.
- Request amounts are checked (`Postings.CheckAmount`) before the balance check; the minor units come from `currency_ref`.
- Tests: `Support/AccountsApp` (on `BankServiceApp`) delivers the business date, SEK/EUR/USD and one customer at start.

## Commands

```bash
dotnet build && dotnet test                         # integration tests need Docker
dotnet tool restore && dotnet ef migrations add <Name> --project src/Bank.Accounts.Api --output-dir Data/Migrations
```
