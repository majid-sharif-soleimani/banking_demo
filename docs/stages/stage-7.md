# Stage 7: Gateway and data import

Goal: one entry point for all APIs, and the system loaded with about 100 customers and 1 to 2 years of history imported through that entry point.

Read first: design plan section 7; `docs/implementation-decisions.md` (APIs).

## Learning

| Concepts | Decisions for Majid (D1) | Majid writes |
| --- | --- | --- |
| API gateway and reverse proxy, correlation ids and distributed tracing, ETL, idempotent replay of historical data | How the import knows that end of day finished before it continues (poll the business date, or wait for an event); how fast and how parallel the replay runs | Customer selection and date shifting in the import tool, with tests |

Slices: (1) gateway routing and headers, (2) aggregated OpenAPI and health, (3) import reading and selection, (4) day-by-day replay, (5) report and verification.

## Folders created

`core_banking/bank-gateway`, `core_banking/bank-data-import`.

## Gateway

- YARP, routes `/api/<service>/{**rest}` to each service, discovered through Aspire service discovery.
- Simulated authentication: requests must carry `X-Customer-Id` or `X-Performed-By`; the gateway forwards them and adds `X-Correlation-Id`.
- Aggregated OpenAPI: `/openapi` lists each service's document.
- Health endpoint aggregates downstream health.
- Add to the AppHost; all external traffic goes through it.

## Data import

Input: the CSV export of the databasesample.com bank sample database, 100K size, placed by Majid in `bank-data-import/data/raw/` (gitignored). If the folder is empty, the tool stops with a message telling Majid where to download it. Do not try to download it.

Console app `Bank.DataImport`:

0. Activate SEK, EUR and USD through `POST /currencies/{code}/activate` (they are seeded inactive; bank-operations ADR 0002). Activating an active currency is a no-op, so a re-run is safe.
1. Read customers, accounts, transactions, loans, loan payments, standing orders.
2. Select 100 customers: at least 30 with a loan, at least 10 with late payments, at least 15 with standing orders, the rest random with a fixed seed.
3. Shift all dates so the latest event lands on `Operations:StartDate` + import span; keep at most the last 2 years.
4. Convert amounts to SEK with a fixed factor (configurable, default 10.0). Ignore branch data. Map loan types to CAR, MORTGAGE or PURCHASE by keyword, default PURCHASE.
5. Replay day by day through the gateway: open accounts, deposits, withdrawals, transfers, standing orders, loan applications with immediate review, approval and disbursement, then close the day. Let the system generate installments; apply dataset payments as deposits to the repayment account on their dates so automatic collection pays them. Missing payments become arrears naturally.
6. Use Idempotency-Key = a hash of the source row, so re-running the import is safe.
7. Write `data/import-report.md`: counts, skipped rows and why, payment amount mismatches.
8. Verify at the end: each account balance equals its statement sum; trial balance is zero per currency.

Import into a fresh database only; the tool refuses if customers already exist unless `--force` is given.

## Tests (minimum)

Unit: selection, date shifting, amount conversion, loan type mapping, idempotency key. Integration: import a 5-customer fixture CSV through the AppHost and pass the verification step.

## Done when

```bash
for r in bank-gateway bank-data-import bank-platform; do (cd core_banking/$r && dotnet build && dotnet test); done
```
