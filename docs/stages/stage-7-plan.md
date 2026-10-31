# Stage 7 plan: Gateway and data import

## Context

Stages 2–6 are Ready for review (not merged). Stage 7 gives the bank **one entry point**, a YARP gateway in front of every service. It then fills the bank with about 100 customers and their history, imported **through that entry point**, so every rule, event and journal entry runs exactly as in real operations (design plan section 7). The hard parts:

- the gateway must forward headers and add a correlation id without knowing the services' internals;
- the import must replay history day by day, and wait for each end of day across services before going on;
- a re-run must not duplicate anything (idempotency keys from the source rows);
- at the end, the books must verify: every balance equals its statement, and the trial balance is zero.

Environment: `~/repos/banking_demo` on `<machine>`, branch `stage-7-gateway-import` (from `stage-6-cards-ledger`).

**Working mode (Majid, 2026-10-31):** Claude writes everything, with a learning document per slice. D1 decisions are made by Claude under Majid's delegation.

## Slices

1. **Gateway routing and headers.** `bank-gateway` (YARP):
   - routes `/api/<service>/{**rest}` to each service through Aspire service discovery;
   - requires `X-Customer-Id` or `X-Performed-By` (401 otherwise);
   - forwards both and adds or keeps `X-Correlation-Id`.
2. **Aggregated OpenAPI and health.** `/openapi` lists each service's document; `/health` includes every downstream service.
3. **Import reading and selection.** `bank-data-import` (console app `Bank.DataImport`):
   - reads the CSV export (`data/raw/`, gitignored) through a column mapping;
   - **selection, date shifting, amount conversion, loan type mapping and the idempotency key** are pure and tested.
4. **Day-by-day replay** through the gateway:
   - activate the currencies, register customers, open accounts;
   - each day: deposits, withdrawals, transfers, standing orders, loan applications (review, approve, disburse), and loan payments as deposits;
   - close the day and wait until the business date advances.
5. **Report and verification:** `data/import-report.md` (counts, skipped rows and why, mismatches), each account balance against its statement, and the trial balance zero per currency. An integration test imports a 5-customer fixture through the real AppHost.

## Decisions to review (Majid's delegation of 2026-10-31)

| Decision (D1 in the stage file) | ADR | Chosen | Main alternative |
| --- | --- | --- | --- |
| How the import knows end of day finished | bank-data-import 0001 | **Poll** `GET /api/operations/business-days/current` until it shows the next date Open (with a timeout) | Wait for `BusinessDayOpened` on Kafka (the tool would become a Kafka consumer) |
| How fast and how parallel the replay runs | bank-data-import 0001 | **Sequential**: one day after another, one request after another within a day | Customers in parallel within a day |

Interpretations and gaps:

- **The dataset's column names are not in the repo.** The reader uses a configurable column mapping (`appsettings.json`, `Import:Columns`). The fixture uses the default names, which must be checked against Majid's real export before the first real run.
- An amount or term outside the product range is **skipped and reported**, never clamped.
- A PURCHASE loan gets equal stages: at most `MaxStages`, about one per quarter, the last one at maturity.

## Done when

```bash
for r in bank-gateway bank-data-import bank-platform; do (cd core_banking/$r && dotnet build && dotnet test); done
```
