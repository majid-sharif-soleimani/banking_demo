# Stage 6: Cards and Ledger

Goal: installments can be paid manually with a test card, every financial event produces a balanced journal entry, and the reports in the design plan exist.

Read first: design plan section 2 (Cards, Accounting and reporting), 6 (sample journal entries).

## Learning

| Concepts | Decisions for Majid (D1) | Majid writes |
| --- | --- | --- |
| Double-entry bookkeeping as code, read models and projections, a light form of CQRS, idempotent posting | How report read models are built (projection tables updated by handlers, or queries over the service's own tables); how a posting rule is represented (one function per event, or a data-driven rule table) | The posting rules in `Logic/` with tests that every entry balances per currency; the trial balance query |

Slices: (1) Cards, (2) Ledger chart of accounts and posting rules, (3) posting handlers and trial balance, (4) Lending reports.

## Folders created

`core_banking/bank-cards`, `core_banking/bank-ledger`. Add both to the AppHost.

## Cards

Data: `test_card` (seeded, see implementation decisions), `card_payment`.

Endpoint: `POST /card-payments` with card number, amount, purpose `LoanInstallment`, loan id, optional schedule item number, Idempotency-Key. Behavior by card: Approve → `CardPaymentCaptured`; Decline → `CardPaymentDeclined` (reason declined); InsufficientFunds → `CardPaymentDeclined` (reason insufficient_funds). Unknown card → 422. The card has no balance; it represents an external card.

Lending already handles `CardPaymentCaptured`. Customers handles `CardPaymentDeclined` with an email.

## Ledger

Data: `gl_account` (seeded per active currency), `journal_entry`, `journal_line`. Unique `source_message_id` makes posting idempotent.

Chart of accounts (one per currency):

| Code | Name | Type |
| --- | --- | --- |
| 1000 | Cash | Asset |
| 1100 | CardClearing | Asset |
| 1200 | LoansReceivable | Asset |
| 2000 | CustomerDeposits | Liability |
| 2900 | FxPosition | Liability |
| 4000 | InterestIncome | Income |
| 4100 | PenaltyIncome | Income |

Postings:

| Event | Debit | Credit |
| --- | --- | --- |
| MoneyDeposited | Cash | CustomerDeposits |
| MoneyWithdrawn | CustomerDeposits | Cash |
| MoneyTransferred, same currency | no entry | |
| MoneyTransferred, two currencies | CustomerDeposits (source) / FxPosition (target) | FxPosition (source) / CustomerDeposits (target) |
| LoanDisbursed | LoansReceivable | CustomerDeposits |
| LoanPaymentAllocated, source Account or Payoff | CustomerDeposits (total) | LoansReceivable (principal), InterestIncome (interest), PenaltyIncome (penalty) |
| LoanPaymentAllocated, source Card | CardClearing (total) | LoansReceivable, InterestIncome, PenaltyIncome |

Ledger does not post from `PaymentCollected`, `CardPaymentCaptured` or `LoanPaidOffEarly`; the allocation split exists only in `LoanPaymentAllocated`. Waived future interest was never recognized (cash basis), so it has no entry.

Endpoints: `GET /journal-entries?from=&to=`, `GET /trial-balance?date=&currency=`.

Reports (read models built from events inside the owning service, no cross-service queries):

| Report | Service | Endpoint |
| --- | --- | --- |
| Loan portfolio (count, outstanding by product) | Lending | GET /reports/portfolio?date= |
| Arrears by classification | Lending | GET /reports/arrears?date= |
| Trial balance | Ledger | GET /trial-balance |

## Tests (minimum)

Unit: posting rules per event, every entry balances per currency. Integration: each event type posts once; duplicate event posts nothing; trial balance sums to zero after a mixed scenario; card approve/decline/insufficient flows end to end with Lending.

## Done when

```bash
for r in bank-cards bank-ledger bank-lending bank-customers bank-platform; do (cd core_banking/$r && dotnet build && dotnet test); done
```
