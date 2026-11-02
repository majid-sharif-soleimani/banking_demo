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

## Close-out (2026-10-31)

- Done-when: gateway 9, data-import 33 + 1, platform 47, all green. The import integration test passed 5 runs in a row after the Kafka fix (about 40 s each).
- **A messaging bug surfaced here and was fixed in `bank-contracts` 0.6.1:** a consumer that subscribed to a just-created topic could miss it for 5 minutes (librdkafka's default metadata refresh). `KafkaTopicInitializer` now waits until its topics have partition leaders, and the consumer refreshes metadata every 10 s. All six services moved to the 0.6.1 packages, and their tests are green.
- `banking-reviewer`: five REQUIRED findings, all fixed:
  1. A second run (or `--force`) closed every day without registering anyone, aging imported loans into default. It is now refused at the first existing customer; no account opened means no day closed; verification fails with no account checked; the test runs the import twice.
  2. Selection traits now count only rows inside the two-year window; the report shows the reached count against each target.
  3. Loan types map by whole word.
  4. The PURCHASE stage cap comes from the product (`GET /loan-products`).
  5. Unknown frequencies and account types are skipped and reported, not guessed.

  OPTIONAL items applied: a partial report when the import stops; loans before transactions within a day; one date anchor; the gateway refuses a blank `X-Performed-By` and replaces an unsafe correlation id; the ADR's retry wording; CI paths for data-import. Not applied (listed under "What to watch later" in the learning docs): a GL-to-subledger reconciliation, the mismatch check's reference amount, correlation-id logging in the services, tests for the Kafka fix.
- More interpretations to review: standing orders that started before the cut-off are skipped, not started at the first kept day; "one stage per quarter" for PURCHASE.
- To review: bank-data-import ADR 0001. `/retro 7` pending. Not merged; stage 8 continues on `stage-8-e2e-aws`.

## Done when

```bash
for r in bank-gateway bank-data-import bank-platform; do (cd core_banking/$r && dotnet build && dotnet test); done
```

## Sign-off (2026-11-02)

- `master` (closed stages 2–6) merged into this branch with no code conflict. Re-run after the merge: gateway 9, data-import 34 (33 unit, 1 end-to-end import), platform 47, lending 1,621, ledger 28, cards 13, accounts 120, customers 58, operations 61 tests green.
- Majid confirmed data-import ADR 0001 (poll the business date through the gateway; sequential replay) and the interpretations above: out-of-range amounts or terms are skipped and reported, and PURCHASE loans get equal quarterly stages up to `MaxStages`.
- Still open for Majid: check the default column mapping (`Import:Columns`) against his real databasesample.com export before the first real run.

