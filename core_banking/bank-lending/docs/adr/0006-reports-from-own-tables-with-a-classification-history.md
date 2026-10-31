# 0006. Build Lending reports from its own tables, plus a classification history

- Status: Accepted (to be reviewed by Majid)
- Date: 2026-10-31
- Scope: bank-lending
- Design plan reference: section 2 (bank reports: loan portfolio, arrears by classification); stage 6 file (read models inside the owning service)
- Decided by: Claude on Majid's delegation of 2026-10-31. A D1 for Majid in the stage 6 file.

## Context

The bank needs a loan portfolio report (count and outstanding by product) and an arrears report (by classification), each as of a business date. The data lives in Lending, and reports may not query other services. A report for a past date needs the state on that date, not today's.

## Decision

- **Portfolio:** a query over Lending's own tables. Running loans are those with `disbursed_on ≤ D` and `closed_on` null or after D (`closed_on` is a new column, set when the loan is paid off). Outstanding = principal − principal of `loan_payment` rows booked on or before D.
- **Arrears:** the same running loans, classified by their last `loan_classification_change` on or before D, or Current before any change. The end of day writes that small table next to `LoanClassificationChanged`.
- Both endpoints are staff only.

## Rationale

The payments table already records the principal part of each payment by business date, so the outstanding amount at any date is one query, with no extra state. Classification is the only fact that is overwritten in place, so it is the only one that needs a history. Keeping that history to the changes keeps it tiny.

## Alternatives considered

- **Projection tables for every report, updated by handlers** (for example a daily snapshot per loan): faster reads for a large portfolio, but more tables, more writes on every event, and snapshots to rebuild when a rule changes. Rejected for a demo with about 100 customers.
- **Reports of today's state only:** simplest, but the stage file asks for `?date=`. Rejected.

## Consequences

- Report queries scan loans and payments. That is fine at this size; a snapshot projection is the next step if it grows.
- `closed_on` and `loan_classification_change` are now part of Lending's data contract for reports and must be kept up to date by the payment and end-of-day code.
