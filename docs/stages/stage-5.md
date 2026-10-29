# Stage 5: Lending service, documents and appointments

Goal: loan products, applications with all statuses, disbursement, automatic installment collection, overdue handling, penalties, classification and early payoff work end to end; Customers stores documents, information requests and appointments.

Read first: design plan sections 2 (Loans, Cards documents appointments), 5, 6; `docs/stages/stage-3.md` for the logic already built.

## Learning

| Concepts | Decisions for Majid (D1) | Majid writes |
| --- | --- | --- |
| Workflow state machines, long-running processes across services, choreography with many events, business keys for idempotency, failure and compensation paths | How application status transitions are modeled (a transition table in `Logic/`, a library such as Stateless, or checks in each endpoint); the unique business key that prevents a double debit for one installment | The application state machine with tests for every allowed and rejected transition; the `PaymentCollected` handler in Lending; the integration test "duplicate InstallmentDue debits once" |

Slices: (1) products and applications, (2) disbursement and schedule, (3) end of day: due installments, penalties, classification, (4) Accounts handlers and payments, (5) early payoff, (6) Customers documents, information requests and appointments.

## Lending (extends the stage 3 folder)

Data: `loan_product` (seeded), `loan_application` (+ status history as a child table), `loan`, `schedule_item`, `loan_payment`, `customer_ref`, `account_ref`, `business_day`.

Application statuses and allowed transitions:

```
Submitted → UnderReview → InfoRequested → UnderReview (on InformationReceived)
UnderReview → AppointmentScheduled → UnderReview (after the appointment date passes or on demand)
UnderReview → Approved → Disbursed
Submitted | UnderReview | InfoRequested | AppointmentScheduled → Rejected | Cancelled
```

The system does not decide approval. Review actions are API calls (made by the agent in phase 2, by tests now).

Endpoints:

| Method | Route | Notes |
| --- | --- | --- |
| GET | /loan-products | list |
| POST | /loan-applications | validates amount and term against the product; staged products require a stage plan |
| GET | /loan-applications/{id} | with status history |
| POST | /loan-applications/{id}/review | move to UnderReview |
| POST | /loan-applications/{id}/information-requests | list of requested items → InfoRequested, publishes InformationRequested |
| POST | /loan-applications/{id}/approve | → Approved |
| POST | /loan-applications/{id}/reject, /cancel | with reason |
| POST | /loan-applications/{id}/disburse | creates the loan and schedule, publishes LoanDisbursed |
| GET | /loans?customerId=, /loans/{id} | with schedule, outstanding principal, overdue amount, days past due |
| GET | /loans/{id}/payoff-quote?date= | from EarlyPayoff.Quote |
| POST | /loans/{id}/payoff | quotes for today, publishes LoanPayoffRequested with the quote amount |

End of day on `DayClosed(D)`, in this order: publish `InstallmentDue` for items due on D and for every unpaid overdue item (daily retry); add penalties for D; update classification and publish `LoanClassificationChanged` when it changes; publish `PenaltyAccrued` when a penalty was added; then `DayCompleted(D, "lending")`. Add `lending` to `EndOfDayParticipants`.

Handlers: `PaymentCollected` (purpose Installment) → Allocation.Apply, mark items Paid, loan PaidOff when all paid, publish `LoanPaymentAllocated`; `PaymentCollected` (purpose Payoff) → settle every item, waive future interest, loan PaidOffEarly, publish `LoanPaidOffEarly` and `LoanPaymentAllocated`; `PaymentFailed` → item stays unpaid (classification happens at end of day); `CardPaymentCaptured` → same as an installment PaymentCollected, with source Card (Cards arrives in stage 6; add the handler and schema now); `InformationReceived` → back to UnderReview; `AppointmentScheduled` → status AppointmentScheduled.

Accounts changes: handle `LoanDisbursed` (credit the repayment account), `InstallmentDue` (debit if balance covers the full amount due, else `PaymentFailed`; no partial debits), `LoanPayoffRequested` (debit the full amount and publish `PaymentCollected` with purpose Payoff, or `PaymentFailed` with purpose Payoff if the balance is insufficient; the loan then stays Active).

`PaymentCollected` and `PaymentFailed` carry: LoanId, ScheduleItemNumber (null for payoff), Purpose (Installment, Payoff), Amount, Reason (failed only).

`LoanPaymentAllocated` carries: LoanId, Source (Account, Card, Payoff), Principal, Interest, Penalty, all Money. Ledger posts from this event only (stage 6).

## Customers additions

Data: `employee` (seeded E001–E005), `interaction`, `information_request`, `appointment`.

Endpoints: `POST /customers/{id}/interactions` (multipart upload, email reply text, or note; optional application id; files through `IFileStorage`), `GET /customers/{id}/interactions`, `GET /information-requests?applicationId=`, `POST /information-requests/{id}/complete` (publishes InformationReceived), `GET /appointments/availability?date=`, `POST /appointments` (publishes AppointmentScheduled), `DELETE /appointments/{id}`.

Handlers: `InformationRequested` → create the request and email; `LoanApplicationStatusChanged` → interaction record; `PaymentFailed` and `LoanClassificationChanged` → email.

## Tests (minimum)

Integration: full annuity loan lifecycle across 3 simulated months with all payments; insufficient balance → overdue → penalty from day 6 → paid next day; classification to Default after 90 days; staged loan with 3 stages; early payoff quote and payoff; payoff with insufficient balance leaves the loan Active; every application transition and every rejected transition; duplicate InstallmentDue debits once; upload stored and retrievable; appointment double-booking rejected.

## Done when

```bash
for r in bank-lending bank-customers bank-accounts bank-operations bank-platform; do (cd core_banking/$r && dotnet build && dotnet test); done
```
