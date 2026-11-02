# 0002. Book unallocated loan payments as owed to the customer

- Status: Accepted (reviewed and confirmed by Majid on 2026-11-02; a business decision)
- Date: 2026-10-31
- Scope: bank-ledger, bank-lending, bank-contracts
- Design plan reference: section 2 (Accounting: double-entry, cash basis; Cards); stage 6 file (chart of accounts, postings)
- Decided by: Claude on Majid's delegation of 2026-10-31, after the stage 6 review found the gap.

## Context

A card payment is captured by Cards before Lending sees it. Lending cannot allocate to a closed loan, and since 2026-11-02 it prepays later installments but returns what is left once every item is paid (bank-lending ADR 0003, point 5). So part or all of a card payment can stay unallocated. `LoanPaymentAllocated` carried only the allocated parts, so Ledger booked less than the card network settled: CardClearing was short and the books no longer matched reality. The stage 6 chart has no account for such money.

## Decision

- `LoanPaymentAllocated` gets an optional `Unallocated` (schema 1-0-1, an ADDITION; `bank-contracts` 0.6.0). Lending fills it with whatever `Allocation.Apply` could not place. A card payment for a closed loan allocates nothing, and the whole amount is unallocated.
- The chart gets **2100 CustomerPayable** (Liability). The payment rule debits the source (CardClearing or CustomerDeposits) with the full amount received, and credits the allocated parts plus CustomerPayable for the unallocated part.
- A refund process is outside phase 1. The balance of CustomerPayable is the list of money to return.

## Rationale

Money received must be in the books. Booking it as a liability to the customer is the conservative choice: no income is recognized for it, and the bank's debt is visible in the trial balance.

## Alternatives considered

- **Reject the card payment in Cards when nothing is due:** Cards does not know the schedule, and would need a copy of every loan's state. It also cannot stop a race with an end-of-day collection. Rejected as the only protection; it may be added later.
- **Allocate the excess to future installments (prepayment):** contradicts "overpayment is returned, not applied to future items" (stage 3 file). Rejected.

## Consequences

- The chart has eight accounts instead of the stage file's seven. Existing charts get the new account on the next posting in that currency (`Chart.EnsureAsync`).
- A refund feature will need to debit CustomerPayable when the money is paid back.
- Prepaid interest (Majid, 2026-11-02): since an overpayment prepays later installments, `LoanPaymentAllocated` can carry interest of items not yet due. Ledger stays on a cash basis and books it as interest income when received, like any other interest. No unearned-interest liability and no transfer at the due date. A later early payoff does not give prepaid interest back (bank-lending ADR 0003), so nothing has to be reversed.
- A payment while an early payoff is pending (Majid, 2026-11-02): Lending allocates nothing and the whole amount is unallocated, booked here as CustomerPayable. The payoff settles exactly its quoted principal, interest and penalty, so money allocated meanwhile would be counted twice, in the loan receivable and in Lending's reports. This covers a card payment and an installment collection opened before the payoff request.

