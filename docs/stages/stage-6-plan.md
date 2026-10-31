# Stage 6 plan: Cards and Ledger

## Context

Stages 2–5 are Ready for review (not merged). Stage 6 adds two services and the bank's reports.

- **Cards** simulates an external card: a few test card numbers with fixed behaviour. A customer can pay a loan installment manually, and Lending already allocates `CardPaymentCaptured`.
- **Ledger** turns every financial event into a balanced double-entry journal entry, per currency. It works on a cash basis: interest and penalties are recognized when they are collected (design plan section 2).
- **Reports** are built inside the service that owns the data: the loan portfolio and arrears in Lending, and the trial balance in Ledger.

The hard parts:

- every entry must balance per currency;
- an event must post exactly once;
- the trial balance must be zero after any mix of events;
- reports must answer "as of a date" without querying another service.

Environment: `~/repos/banking_demo` on `<machine>`, branch `stage-6-cards-ledger` (from `stage-5-lending-service`).

**Working mode (Majid, 2026-10-31):** Claude writes everything, with a learning document per slice. D1 decisions are made by Claude under Majid's delegation and listed below.

## Slices

1. **Cards:**
   - `CardPaymentDeclined` contract (`bank-contracts` 0.5.0);
   - the `bank-cards` service: seeded `test_card` (9999 0000 0000 0001/2/3), `card_payment`, `loan_ref` from `LoanDisbursed`;
   - `POST /card-payments` with an Idempotency-Key: Approve → `CardPaymentCaptured`; Decline or InsufficientFunds → `CardPaymentDeclined`; an unknown card or loan → 422;
   - Customers emails on `CardPaymentDeclined`;
   - the flow end to end with Lending.
2. **Chart of accounts and posting rules:**
   - the `bank-ledger` service, with `gl_account` created per currency on `CurrencyActivated`;
   - `Logic/PostingRules`, one pure function per event, test first, every entry balanced per currency.
3. **Posting handlers and trial balance:**
   - handlers for `MoneyDeposited`, `MoneyWithdrawn`, `MoneyTransferred`, `LoanDisbursed` and `LoanPaymentAllocated`;
   - `journal_entry` unique per (source message, currency);
   - `GET /journal-entries?from=&to=` and `GET /trial-balance?date=&currency=`;
   - a mixed scenario whose trial balance is zero.
4. **Lending reports:**
   - `GET /reports/portfolio?date=` (count and outstanding by product, from loans and payments up to the date);
   - `GET /reports/arrears?date=` (by classification, from a classification history written at end of day).

## Decisions to review (Majid's delegation of 2026-10-31)

| Decision (D1 in the stage file) | ADR | Chosen | Main alternative |
| --- | --- | --- | --- |
| How report read models are built | bank-lending 0006 | Queries over Lending's own tables (loans and payments by business date), plus one small projection, `loan_classification_change`, written at end of day for arrears at a past date | Projection tables for every report, updated by handlers |
| How a posting rule is represented | bank-ledger 0001 | One pure function per event in `Logic/PostingRules`, returning journal lines; a shared check that each entry balances per currency | A data-driven rule table |

Interpretations:

- A cross-currency transfer gives **two entries**, one per currency, each balanced through `FxPosition`. That is why the posting key is (source message id, currency).
- The chart of accounts is created when a currency is activated (`CurrencyActivated`), seven accounts per currency.
- Cards checks that the loan exists and that the amount is in the loan currency, using its own `loan_ref` from `LoanDisbursed`, so a payment for an unknown loan never reaches Lending.

## Close-out (2026-10-31)

- Done-when: cards 13, ledger 28, lending 1,611, customers 58, platform 39, all green.
- `banking-reviewer`: posting rules, idempotency and trial balance are correct. Two REQUIRED findings, both fixed:
  1. The arrears report summed currencies together; it now has one line per classification and currency.
  2. Unallocated card money was missing from the books. `LoanPaymentAllocated` 1-0-1 now carries `Unallocated`, posted to the new account 2100 CustomerPayable (bank-ledger ADR 0002, a business decision for Majid). A card payment for a closed loan allocates nothing.

  OPTIONAL items applied: the Ledger chart is created on first use (event order across topics); Cards answers 422 `currency_not_active` instead of falling back to 2 minor units. Not applied: a backfill of the classification history (dev data only); a cross-service card test (stage 8 end to end).
- To review: bank-ledger ADRs 0001 and 0002, bank-lending ADR 0006. `/retro 6` pending. Not merged; stage 7 continues on `stage-7-gateway-import`.

## Done when

```bash
for r in bank-cards bank-ledger bank-lending bank-customers bank-platform; do (cd core_banking/$r && dotnet build && dotnet test); done
```
