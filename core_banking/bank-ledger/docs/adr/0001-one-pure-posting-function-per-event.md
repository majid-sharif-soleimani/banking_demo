# 0001. One pure posting function per event

- Status: Accepted (to be reviewed by Majid)
- Date: 2026-10-31
- Scope: bank-ledger
- Design plan reference: section 2 (Accounting: double-entry per currency, cash basis), section 6 (sample journal entries)
- Decided by: Claude on Majid's delegation of 2026-10-31. A D1 for Majid in the stage 6 file.

## Context

Ledger turns financial events into double-entry journal entries. Five events post, and each has its own rule. `MoneyTransferred` posts nothing in one currency and two entries, one per currency, across currencies. `LoanPaymentAllocated` debits a different account depending on its source and skips zero parts. The rules must be easy to read against the design plan, and provably balanced.

## Decision

`Logic/PostingRules` has one pure function per event (`Deposit`, `Withdrawal`, `Transfer`, `Disbursement`, `PaymentAllocated`). Each returns a list of `JournalDraft(Currency, Description, Lines)`, and an empty list means no accounting effect. `JournalDraft.IsBalanced` is the shared invariant: at least two lines, and debits equal credits. Tests check every rule's lines against the stage file's table, and that every draft balances. The handlers only map an event to a rule and store the drafts.

## Rationale

The rules differ in shape, not just in account codes. A function per event says exactly what happens, including the special cases, in a few lines that read like the design plan's table. It is pure, so every rule is tested without a database.

## Alternatives considered

- **A data-driven rule table** (event → debit account, credit account): compact for the simple rules, but the transfer (zero or two entries) and the payment split (source-dependent debit, optional lines) need exceptions in code anyway. The table would hide them. Rejected.
- **Posting logic inside each handler:** no separate layer, but nothing pure to test and no single place for the rules. Rejected.

## Consequences

- A new financial event needs a new function and a test, which is a deliberate, reviewable change.
- Ledger trusts the amounts in events. It does not recompute splits, because the allocation lives only in `LoanPaymentAllocated` (stage 6 file).
