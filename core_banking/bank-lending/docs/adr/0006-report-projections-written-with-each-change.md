# 0006. Build each Lending report from its own projection, written with each change

- Status: Accepted (decided by Majid on 2026-11-02)
- Date: 2026-10-31, changed 2026-11-02
- Scope: bank-lending
- Design plan reference: section 2 (bank reports: loan portfolio, arrears by classification); stage 6 file (read models inside the owning service)
- Decided by: Majid on 2026-11-02, a D1 in the stage 6 file. Claude's first proposal (queries over Lending's own tables, see the alternatives) was replaced in his review.

## Context

The bank needs a loan portfolio report (count and outstanding principal by product) and an arrears report (by classification), each as of a business date (`?date=`). The data lives in Lending, and reports may not query other services. A report for a past date needs the state on that date, not today's, and today's report must also work before the day is closed.

## Decision

- Each report has its own projection table, updated by the features that change a loan, **in the same `SaveChangesAsync`** as the change itself:
  - `portfolio_movement` (business date, product, currency, loans ±, outstanding ±);
  - `arrears_movement` (business date, classification, currency, loans ±, outstanding ±).
- The rows are movements, not snapshots (Majid, 2026-11-02):

  | Change | Feature | Portfolio | Arrears |
  | --- | --- | --- | --- |
  | Disbursed | `Disbursement` | +1, +principal | Current: +1, +principal |
  | Principal paid | `LoanPayments.AllocateAsync`, `EarlyPayoffSettlement` | −principal | current class: −principal |
  | Closed (paid off, paid off early) | same | −1 | current class: −1 (before it is reset to Current) |
  | Reclassified | end of day | — | old class: −1, −outstanding; new class: +1, +outstanding |

- `ReportProjections` writes the rows. A report on date D is the sum of its rows with `business_date ≤ D`: portfolio lines with at least one loan; arrears, every classification for each currency with running loans. Both endpoints are staff only.
- `loan_classification_change` from the first version is gone. The arrears projection holds the history it was kept for.

## Rationale

- One table per report keeps each read a single grouped sum, with no join over loans, payments and a classification history.
- Writing the movement in the same transaction as the change means the projection can never disagree with the loan, and it inherits the change's idempotency: the end of day is guarded by `loan_day_run`, a payment by its unique `source_id`, and disbursement by the application's status transition. A replayed event writes nothing twice.
- Movements answer any date, today included. Daily snapshots would only answer for closed days, and they grow with the number of days, not the number of changes.

## Alternatives considered

- **Queries over Lending's own tables plus a classification history** (Claude's first proposal): no extra writes and one source of truth. But every read joins loans, payments and the history, and each new report adds such a query. Replaced by Majid on 2026-11-02.
- **A daily snapshot per report, written at end of day:** simplest to read, but there is no report for today before the day closes, and it grows every day. Not chosen.
- **Projection handlers on Lending's own events (from the outbox):** a looser coupling, but the projection would lag the loan and need its own inbox. Not needed inside one service.

## Consequences

- Every feature that changes a loan's principal, status or classification must call `ReportProjections`. A missing call makes a report wrong without failing the feature. `ReportTests` checks every row type and, after each scenario, an invariant per loan: a running loan's rows sum to 1 loan and its unpaid principal in its current classification only, a closed loan's to 0 and 0. Writing `Closed` after the reset was checked to fail it.
- A principal movement always writes the loan row (`PerformedBy`), so a payment and a concurrent end of day meet on the loan's `xmin` and the loser retries on fresh data; without it, two Lending instances could skew the projection with a stale classification or outstanding amount (review of 2026-11-02).
- A new report needs a new projection table and calls in the features that change what it shows.
- Migration `ReportProjections` drops `loan_classification_change` and starts both projections empty. Local databases created before 2026-11-02 must be recreated (nothing is deployed). There is no backfill.
