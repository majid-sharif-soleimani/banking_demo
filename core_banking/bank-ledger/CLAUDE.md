# bank-ledger

Double-entry general ledger, per currency, posted only from events (design plan section 2; cash basis). Schema `ledger`, publishes nothing. Plan: `docs/stages/stage-6-plan.md`.

## Tables

- `gl_account`: (code, currency); the seven accounts of the chart are created when `CurrencyActivated` arrives.
- `journal_entry`: one per (source message id, currency), unique; business date = the event's `OccurredOn`.
- `journal_line`: debit or credit (check constraint), FK to `gl_account`.

## Posting

- `Logic/PostingRules`: one pure function per event (ADR 0001); `JournalDraft.IsBalanced`. Handlers in `Features/Posting.cs` map event → rule → `Posting.PostAsync` (idempotent by message id; unbalanced → throws).
- Posts from `MoneyDeposited`, `MoneyWithdrawn`, `MoneyTransferred` (only across currencies, two entries through FxPosition), `LoanDisbursed`, `LoanPaymentAllocated` (Account/Payoff → CustomerDeposits, Card → CardClearing; `Unallocated` → 2100 CustomerPayable, ADR 0002).
- The chart (8 accounts) is created on `CurrencyActivated` or on first use, whichever comes first. Never from `PaymentCollected`, `CardPaymentCaptured` or `LoanPaidOffEarly`.
- `GET /journal-entries?from=&to=`, `GET /trial-balance?date=&currency=` (date defaults to the business date).

## Commands

```bash
dotnet build && dotnet test
```
