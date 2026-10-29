# Stage 4: Customers and Accounts

Goal: customers can be registered, accounts opened, money deposited, withdrawn and transferred (also across currencies), standing orders run at end of day, and statements and balance-at-date work.

Read first: design plan section 2 (Customers, Accounts), 5, 6; `docs/implementation-decisions.md` (APIs, seed data, external services).

## Learning

| Concepts | Decisions for Majid (D1) | Majid writes |
| --- | --- | --- |
| Optimistic concurrency, API idempotency keys, local copies of another service's data (`customer_ref`, `currency_ref`), eventual consistency, RFC 9457 problem details, append-only entries versus a stored balance | What happens when an account is opened for a customer whose `CustomerRegistered` event has not arrived yet (reject, accept and verify later, or wait); how the balance is stored (column updated with each entry, or always computed from entries) | `StandingOrders.NextRunDate` and `Fx.Convert` with tests; the transfer feature (endpoint, FX, both entries); the integration test "two concurrent withdrawals cannot overdraw" |

Slices: (1) Customers registration and notification, (2) accounts, reference data and deposits/withdrawals, (3) transfers with FX, (4) statements and balance at date, (5) standing orders at end of day.

## Folders created

`core_banking/bank-customers` and `core_banking/bank-accounts`. Add both to the AppHost. Accounts joins end of day (`EndOfDayParticipants` already lists it).

## Customers (this stage: registration and notifications only)

Data: `customer` (id, name, national_id unique, date_of_birth, email, phone, address, preferred_language default `en`, status), `notification` (id, customer_id, channel, template, payload, status, sent_at).

Endpoints: `POST /customers`, `GET /customers/{id}`, `PATCH /customers/{id}` (contact details only).

Publishes `CustomerRegistered`. Handles `StandingOrderFailed` → email notification via `IEmailSender` with an English template.

Employees, interactions, information requests and appointments are stage 5.

## Accounts

Data: `account`, `account_entry`, `transfer`, `standing_order`, `standing_order_run`, local copies `customer_ref` and `currency_ref`, `business_day`.

Endpoints:

| Method | Route | Notes |
| --- | --- | --- |
| POST | /accounts | customer id, type (Current, Savings), currency (must be active) |
| GET | /accounts?customerId= | list |
| GET | /accounts/{id} | balance |
| POST | /accounts/{id}/deposits | cash deposit, Idempotency-Key |
| POST | /accounts/{id}/withdrawals | no overdraft → 422 insufficient_funds |
| POST | /transfers | from, to, amount in source currency; converts with `IExchangeRateProvider` when currencies differ; stores rate, source, time |
| GET | /accounts/{id}/statement?from=&to= | entries with balance after |
| GET | /accounts/{id}/balance?date= | balance at end of that business date |
| POST | /standing-orders | can be created without balance |
| GET/DELETE | /standing-orders/{id} | cancel sets status Cancelled |

End of day (on `DayClosed(D)`): run standing orders with next run date = D, each in its own transaction; on insufficient funds record a failed run and publish `StandingOrderFailed`; no retry; advance next run date either way; then publish `DayCompleted(D, "accounts")`.

Events published: `AccountOpened`, `MoneyDeposited`, `MoneyWithdrawn`, `MoneyTransferred`, `StandingOrderFailed`. Add schemas with `/new-event`, bump `bank-contracts` minor version.

Concurrency: balance updates use the row version; a conflicting update retries the command once, then returns 409.

Logic (pure, TDD): `StandingOrders.NextRunDate(order, after)`, `Fx.Convert(money, rate, targetCurrency, minorUnits)`, `Balances.AtDate(entries, date)`.

## Tests (minimum)

Unit: next run date for each frequency including the 31st; FX conversion rounding; balance at date. Integration: deposit/withdraw/transfer happy paths; overdraft rejected; cross-currency transfer stores the rate; repeated Idempotency-Key returns the same result; two concurrent withdrawals cannot overdraw; standing order success and failure at end of day; StandingOrderFailed produces one email; DayCompleted published once per day.

## Done when

```bash
for r in bank-customers bank-accounts bank-platform; do (cd core_banking/$r && dotnet build && dotnet test); done
```
