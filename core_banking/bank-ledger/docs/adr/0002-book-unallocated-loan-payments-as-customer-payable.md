# 0002. Book unallocated loan payments as owed to the customer

- Status: Accepted (to be reviewed by Majid; a business decision)
- Date: 2026-10-31
- Scope: bank-ledger, bank-lending, bank-contracts
- Design plan reference: section 2 (Accounting: double-entry, cash basis; Cards); stage 6 file (chart of accounts, postings)
- Decided by: Claude on Majid's delegation of 2026-10-31, after the stage 6 review found the gap.

## Context

A card payment is captured by Cards before Lending sees it. Lending never prepays future installments (bank-lending ADR 0003) and cannot allocate to a closed loan. So part or all of a card payment can stay unallocated. `LoanPaymentAllocated` carried only the allocated parts, so Ledger booked less than the card network settled: CardClearing was short and the books no longer matched reality. The stage 6 chart has no account for such money.

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
