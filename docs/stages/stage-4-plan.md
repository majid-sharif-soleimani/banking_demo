# Stage 4 plan: Customers and Accounts

## Context

Stages 2 and 3 are Ready for review (not merged). Stage 4 adds the first two customer-facing services. **Customers** registers people and sends them notifications. **Accounts** opens accounts and moves money: deposits, withdrawals, transfers (also across currencies), standing orders at end of day, statements and balance at a date. Accounts is also the first real end-of-day participant, so the Operations cycle from stage 2 starts to matter. The hard parts:

- money must never be created or lost under concurrent requests;
- a retried request must not move money twice (Idempotency-Key);
- each service must work from its own copies of other services' data (`customer_ref`, `currency_ref`, `business_day`), which arrive *eventually*.

Environment: `~/repos/banking_demo` on `<machine>`, branch `stage-4-customers-accounts` (from `stage-3-lending-logic`).

**Working mode (Majid, 2026-10-31):** Claude writes everything, including the "Majid writes" items, test first for `Logic/`, with a learning document per slice. D1 decisions are made by Claude under Majid's delegation and listed under "Decisions to review".

## Slices

### 4.1 Customers: registration and notifications
- **Contracts:** `CustomerRegistered(CustomerId, Name, PreferredLanguage)` and `StandingOrderFailed(StandingOrderId, CustomerId, AccountId, Amount, RunDate, Reason)`. `bank-contracts` 0.3.0.
- **Folder:** `core_banking/bank-customers` (service layout), AppHost entry with Mailpit, CI workflow.
- **Data:** `customer` (unique `national_id`), `notification` (unique source message id), `business_day` (consumer).
- **Endpoints:** `POST /customers`, `GET /customers/{id}`, `PATCH /customers/{id}` (email, phone, address, preferred language).
- **Handler:** `StandingOrderFailed` → English email through `IEmailSender`, recorded in `notification`, at most once per event.
- **Email:** `SmtpEmailSender` (Mailpit locally) behind `IEmailSender`; the in-memory fake in tests.

### 4.2 Accounts: reference data, accounts, deposits and withdrawals
- **Contracts:** `AccountOpened`, `MoneyDeposited`, `MoneyWithdrawn`.
- **Folder:** `core_banking/bank-accounts`, AppHost entry, CI workflow.
- **Logic (TDD):** `Luhn.CheckDigit`, `Luhn.IsValid`; `RandomAccountNumberGenerator` (8 random digits, first 1–9, plus Luhn).
- **Data:** `account` (stored balance, `xmin`), `account_entry` (append-only, balance after), `customer_ref`, `currency_ref`, `business_day`, `idempotency_record`.
- **Endpoints:** `POST /accounts`, `GET /accounts?customerId=`, `GET /accounts/{id}`, `POST /accounts/{id}/deposits`, `POST /accounts/{id}/withdrawals` (422 `insufficient_funds`).
- **Concurrency:** `xmin` on the account; one retry, then 409. **Idempotency:** `Idempotency-Key` on every money POST.

### 4.3 Transfers with FX
- **Contracts:** `MoneyTransferred` (with an optional fee for later). `IExchangeRateProvider` returns an `ExchangeRate` (rate, source, observed on).
- **Logic (TDD):** `Fx.Convert(money, rate, targetCurrency, minorUnits)`.
- **Endpoint:** `POST /transfers`. It stores the rate, source and time. Providers: `FixedExchangeRateProvider` (development) and `RiksbankExchangeRateProvider` (SWEA API, `fx_unavailable` when unreachable), chosen by configuration.

### 4.4 Statements and balance at a date
- **Logic (TDD):** `Balances.AtDate(entries, date)`.
- **Endpoints:** `GET /accounts/{id}/statement?from=&to=`, `GET /accounts/{id}/balance?date=`.

### 4.5 Standing orders at end of day
- **Logic (TDD):** `StandingOrders.NextRunDate(order, after)` (weekly, monthly, quarterly; the 31st becomes the last day of shorter months).
- **Endpoints:** `POST /standing-orders`, `GET /standing-orders/{id}`, `DELETE /standing-orders/{id}` (Cancelled).
- **End of day:** on `DayClosed(D)`, run the orders due on D, each in its own transaction, with a unique (order, run date). On insufficient funds: a failed run and `StandingOrderFailed`. Then `DayCompleted(D, "accounts")`, once.

## Decisions to review (Majid's delegation of 2026-10-31)

| Decision | ADR | Chosen | Main alternative |
| --- | --- | --- | --- |
| Account for a customer whose `CustomerRegistered` has not arrived | bank-accounts 0001 | Reject with 422 `customer_not_found`; the caller retries | Accept and verify later (pending accounts) |
| How the balance is stored | bank-accounts 0002 | Stored column on `account`, updated with every entry under `xmin`; entries append-only with `balance_after` | Always computed from entries |
| `IExchangeRateProvider` returns rate, source and observation date | bank-contracts 0017 | `ExchangeRate` record (breaking change to a shared interface, contracts 0.3.0) | Keep `decimal`, take the source from configuration |
| Idempotency-Key storage | bank-accounts 0003 | `idempotency_record` (key, request hash, status, response), saved in the same transaction as the effect; the same key with a different body gives 422 | A cache outside the transaction |

## Close-out (2026-10-31)

- Done-when: customers 37, accounts 115, platform 33, contracts 221 tests green.
- `banking-reviewer`: no money-correctness defect; it independently recomputed the FX results. REQUIRED, fixed: a replay ignored `X-Customer-Id` (now part of the idempotency operation, with a test). OPTIONAL, applied: Idempotency-Key on standing orders; statement `to` after today rejected; a stuck order becomes a `technical_error` failed run; Riksbank per-100 quotes; the catch-up wording documented. Not applied: normalizing amounts before hashing (documented in ADR 0003).
- Infrastructure: `Bank.Testing` 0.3.1 (test PostgreSQL `max_connections=500`, pools cleared on dispose).
- To review: accounts ADRs 0001–0003, contracts ADR 0017. `/retro 4` pending. Not merged; stage 5 continues on `stage-5-lending-service`.

## Done when

```bash
for r in bank-customers bank-accounts bank-platform; do (cd core_banking/$r && dotnet build && dotnet test); done
```
