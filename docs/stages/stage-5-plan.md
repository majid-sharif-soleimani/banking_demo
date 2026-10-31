# Stage 5 plan: Lending service, documents and appointments

## Context

Stages 2–4 are Ready for review (not merged). Stage 5 turns the pure loan logic of stage 3 into a running service and connects it to Accounts, Customers and Operations. A loan is a long-running process that crosses services:

- an application moves through review statuses;
- a disbursement credits the customer's account;
- every end of day asks Accounts to collect due installments, adds penalties and updates the classification;
- payments come back as events and are allocated;
- an early payoff settles everything.

Customers gains the documents, information requests and appointments that the review needs. The hard parts are:

- a state machine that never allows an illegal step;
- money that is never debited twice for one installment, even when events are duplicated or overtake each other;
- an end of day that runs exactly once per loan and date.

Environment: `~/repos/banking_demo` on `<machine>`, branch `stage-5-lending-service` (from `stage-4-customers-accounts`).

**Working mode (Majid, 2026-10-31):** Claude writes everything, including the "Majid writes" items, with a learning document per slice. D1 decisions are made by Claude under Majid's delegation and listed under "Decisions to review".

## Slices

1. **Contracts, products and applications.**
   - The 15 stage 5 events in `bank-contracts` 0.4.0.
   - The Lending service gets its database, messaging and AppHost entry.
   - Seeded products (CAR, MORTGAGE, PURCHASE).
   - `ApplicationStateMachine` in `Logic/`, test first, every allowed and rejected transition.
   - Application endpoints (submit, get with history, review, information request, approve, reject, cancel).
   - `LoanApplicationSubmitted`, `LoanApplicationStatusChanged`, `InformationRequested`.
   - `customer_ref` and `account_ref` from `CustomerRegistered` and `AccountOpened`.
2. **Disbursement and schedule.**
   - `POST /loan-applications/{id}/disburse`: product terms are copied onto the loan, the schedule comes from `Amortization`, and `LoanDisbursed` is published.
   - Accounts credits the repayment account (`LoanDisbursed` handler, entry kind `LoanDisbursement`).
   - `GET /loans?customerId=` and `GET /loans/{id}`, with the schedule, outstanding principal, overdue amount and days past due.
3. **End of day in Lending.**
   - On `DayClosed(D)`, per loan in its own transaction, with a unique (loan, date) run:
     - `InstallmentDue` for items due by D that have no open collection;
     - `Penalties.ForDay`, and `PenaltyAccrued` when a penalty was added;
     - `Classification.For`, and `LoanClassificationChanged` on a change.
   - Then `DayCompleted(D, "lending")`, once per date.
4. **Collections and payments.**
   - Accounts `InstallmentDue`: debit only if the full amount is there, otherwise `PaymentFailed`. A collection is unique per collection id.
   - Lending `PaymentCollected` (Installment) → `Allocation.Apply`, `LoanPaymentAllocated`, PaidOff when everything is paid.
   - Lending `PaymentFailed` → the collection closes and the item stays unpaid.
   - `CardPaymentCaptured` → like an installment payment, with source Card.
5. **Early payoff.**
   - `GET /loans/{id}/payoff-quote?date=`.
   - `POST /loans/{id}/payoff` → `LoanPayoffRequested`.
   - Accounts debits the full amount → `PaymentCollected` (Payoff) or `PaymentFailed` (Payoff).
   - Lending settles every item and waives future interest → PaidOffEarly, `LoanPaidOffEarly`, `LoanPaymentAllocated`.
6. **Customers: documents, information requests and appointments.**
   - Seeded employees E001–E005.
   - Interactions: upload, email reply or note, with files through `IFileStorage`.
   - Information requests: created from `InformationRequested`, plus an email; completing one publishes `InformationReceived`.
   - Appointments: availability, booking with no double booking, which publishes `AppointmentScheduled`, and cancel.
   - Emails for `PaymentFailed` and `LoanClassificationChanged`, and an interaction record for `LoanApplicationStatusChanged`.
   - Lending handles `InformationReceived` and `AppointmentScheduled`.

## Decisions to review (Majid's delegation of 2026-10-31)

| Decision (D1 in the stage file) | ADR | Chosen | Main alternative |
| --- | --- | --- | --- |
| How application transitions are modelled | bank-lending 0004 | A transition table in `Logic/` (pure, tested); endpoints and handlers ask it before changing the status | Stateless library; checks in each endpoint |
| The unique key that prevents a double debit for one installment | bank-lending 0005 | A **collection id**. Lending opens at most one collection per item at a time (`pending_collection_id`) and publishes it in `InstallmentDue`; Accounts records `loan_collection` unique on the collection id and debits once; the next day's retry only happens after `PaymentFailed` closed the collection | (loan, item number) unique in Accounts, which breaks partial card payments and retries |

Interpretations (most conservative):

- **`InstallmentDue` asks for the item's full unpaid amount**, penalty included. Accounts never debits partially (stage file).
- **A collection is booked on Accounts' business date when it is processed**, which may be D+1 if Accounts already completed D.
- **A card payment** (stage 6) is allocated like an installment payment, oldest due items first.
- **An appointment slot** is 60 minutes, 09:00–16:00 Europe/Stockholm, on a day after the current business date (`implementation-decisions.md`).

## Done when

```bash
for r in bank-lending bank-customers bank-accounts bank-operations bank-platform; do (cd core_banking/$r && dotnet build && dotnet test); done
```
