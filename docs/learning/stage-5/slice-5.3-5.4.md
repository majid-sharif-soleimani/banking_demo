# Slices 5.3 and 5.4: End of day in Lending, collections and payments

The two slices were built together, because each one is half of a conversation: Lending's end of day *asks* for money, and Accounts' answers *pay* it.

## Part 1. The big picture

A disbursed loan must be repaid on its due dates. The design plan (section 2) says:

- **Automatic payment:** the installment is debited from the repayment account on the due date. If the balance is short, it becomes overdue and is retried every business day.
- **Penalty:** a daily percentage of the overdue amount, from day 6.
- **Classification:** Overdue from 1 day late, Default from 90 days.
- **Allocation:** penalty, interest, principal, oldest first.

All of it happens across two services. Lending knows what is due, and Accounts holds the money. The hard part is **never debiting twice**, while still asking again every day until the installment is paid.

## Part 2. The building blocks and how they connect

```mermaid
sequenceDiagram
    participant O as Operations
    participant L as Lending end of day
    participant A as Accounts
    participant P as Lending payment handlers
    O->>L: DayClosed(D)
    loop each active loan (own transaction, loan_day_run unique (loan, D))
        L->>L: items due ≤ D, unpaid, no open collection → pending_collection_id = new id
        L-->>A: InstallmentDue(collection id, full unpaid amount)
        L->>L: Penalties.ForDay → PenaltyAccrued
        L->>L: Classification.For → LoanClassificationChanged
    end
    L-->>O: DayCompleted(D, lending) (once, end_of_day_run)
    A->>A: loan_collection(collection id) exists? → nothing
    alt balance ≥ amount
        A->>A: debit (entry LoanRepayment)
        A-->>P: PaymentCollected(collection id)
        P->>P: close collection, Allocation.Apply, loan_payment (source_id unique), LoanPaymentAllocated, PaidOff?
    else
        A-->>P: PaymentFailed(collection id) → close collection; ask again next end of day
    end
```

### 2.1 Lending (`core_banking/bank-lending/src/Bank.Lending.Api/`)

- **`Features/EndOfDay/LendingEndOfDayHandler.cs`**: for each active loan, in its own scope, with one rerun on a version conflict:
  - collection requests: no requests while a payoff is open, otherwise at most one open request per item;
  - penalties for D, then the classification;
  - `loan_day_run(loan, D)`, so penalties are charged once per day.

  After all loans: `end_of_day_run(D)` and `DayCompleted(D, "lending")`, once.
- **`Features/Payments/PaymentHandlers.cs`**:
  - `PaymentCollected` (Installment) closes the collection and allocates the payment as of the later of today and the item's due date;
  - `CardPaymentCaptured` (stage 6) allocates as of today, or to one chosen item;
  - `PaymentFailed` closes the collection;
  - `LoanPayments.AllocateAsync` runs the stage 3 `Allocation.Apply`, writes back the items, records `loan_payment`, publishes `LoanPaymentAllocated` with the principal, interest and penalty split (Ledger posts from it in stage 6), and sets PaidOff when every item is paid.

### 2.2 Accounts (`core_banking/bank-accounts/src/Bank.Accounts.Api/Features/Loans/LoanCollectionHandlers.cs`)

`InstallmentDue` and `LoanPayoffRequested` go through one function. If the collection id has already been answered, it does nothing. Otherwise it debits the full amount (entry `LoanRepayment`) or nothing, publishes `PaymentCollected` or `PaymentFailed` (with the customer id, for Customers' email), and records `loan_collection`. The table and its migration are `loan_collection`, with the collection id as primary key.

## Part 3. Why it is designed this way

- **D1 on your delegation:** one open collection per item, keyed by a collection id (bank-lending ADR 0005). The alternatives were (loan, item) unique, which blocks legitimate second collections, and (loan, item, date), which does not stop the cross-day race.
- **The order inside an end of day** follows the stage file: requests, penalties, classification. So a payment made on day D still leaves D's penalty, collected the next day. The overdue test shows this with real numbers.

| Choice (D2) | Why |
| --- | --- |
| A transaction per loan | One broken loan cannot stop the bank's end of day. |
| Allocation "as of" the item's due date for a collection | Accounts can answer before Lending's clock reaches the due date. |
| A late `PaymentCollected` for a closed collection is still allocated | Money was taken; it must be applied, and it is applied once (`source_id`). |

Patterns: request/response over events with a correlation id; idempotent consumer with business keys (collection id, source id, (loan, date)); unit of work per loan.

## 4. What was tested

**Lending (`LoanLifecycleTests`, 6 tests, with a fake Accounts answering from a test balance):**

- **Three months of a CAR loan:** three debits of 6,170.85, outstanding 184,875.18, three `LoanPaymentAllocated` with the first split being 1,158.33 interest and 5,012.52 principal.
- **Insufficient balance:**
  - asked again every day, with one open request at a time;
  - Overdue from day 1, and no penalty in days 1–5;
  - 3.09 on day 6;
  - after a deposit, 6,173.94 is collected, the day's 3.09 is collected the next day, then Paid and Current again.
- **Default:** Overdue on day 1, Default on day 90 (`DaysPastDue` 90).
- **A staged loan:** three stages collected (31,886.30, 31,271.51, 30,642.74), then PaidOff.
- **The same end of day twice:** one penalty, one request, one `DayCompleted`.
- **A late `PaymentCollected` twice:** allocated once.

**Accounts (`LoanCollectionTests`, 4 tests):**

- a full debit with `PaymentCollected`;
- no partial debit, and `PaymentFailed` with the customer id;
- **a duplicate `InstallmentDue` debits once** (the same message, and the same collection id under a new message id);
- a payoff that fails, and one that is collected, each as a whole.

```
bank-lending IntegrationTests 31 passed; bank-accounts IntegrationTests 68 passed
```

## 5. Check your understanding

1. Why is the collection id stored in Lending *and* used as the primary key in Accounts? What would each alone fail to stop?
2. On 22 Feb the customer has enough money. Why is a penalty of 3.09 still left after that day?
3. Why does each loan run its end of day in its own transaction?
