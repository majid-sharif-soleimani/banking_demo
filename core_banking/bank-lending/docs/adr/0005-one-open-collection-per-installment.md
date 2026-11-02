# 0005. One open collection per installment, keyed by a collection id

- Status: Accepted (accepted by Majid without a detailed review on 2026-11-02)
- Date: 2026-10-31
- Scope: bank-lending (and bank-accounts)
- Design plan reference: section 2 (automatic payment, retried every business day); section 4, ADR row 08 (unique business key for financial effects)
- Decided by: Claude on Majid's delegation of 2026-10-31. A D1 for Majid in the stage 5 file ("the unique business key that prevents a double debit for one installment").

## Context

At each end of day Lending asks Accounts to debit due installments (`InstallmentDue`), and an unpaid installment is asked for again every business day. Three things can cause a double debit:

- the same message delivered twice;
- the same request sent again under a new message id;
- a new day's request sent before the previous day's answer reached Lending, while the installment still looks unpaid there.

A key like (loan, item number) in Accounts stops all three, but it also stops a legitimate second collection for the same item, for example the rest after a partial card payment or after a penalty was added.

## Decision

- Lending gives every collection request a new **collection id** and stores it on the item (`schedule_item.pending_collection_id`, unique). While an item has an open collection, the end of day sends no new `InstallmentDue` for it. No installments are requested at all while a payoff is open (`loan.pending_payoff_collection_id`).
- Accounts answers each collection id at most once: `loan_collection` has the collection id as its primary key, and a second request with the same id is a no-op. It debits the full amount or nothing (`PaymentCollected` / `PaymentFailed`).
- `PaymentCollected` and `PaymentFailed` carry the collection id. Lending closes the open collection, allocates the payment once (`loan_payment.source_id` unique), and the next end of day asks again if the item is still unpaid.

## Rationale

- The key belongs to the *request*, not to the installment, so every legitimate request gets its own key and every repeated request is caught.
- Because there is at most one open request per item, the cross-day race cannot happen: a new request is only sent after the previous one was answered.

## Alternatives considered

- **(loan, item number) unique in Accounts:** simple, but it blocks a legitimate second collection for the same item (the remainder after a penalty or a partial card payment). Rejected.
- **(loan, item, business date):** it stops same-day duplicates but not the cross-day race. Rejected.
- **No guard in Lending, only in Accounts:** Accounts cannot know whether Lending already received the money. Rejected.

## Consequences

- If an answer is lost for good (for example a message dead-lettered), the item keeps its open collection and is not asked again. That is visible as an item past due with `pending_collection_id` set, and it is safer than a double debit.
- Because the end of day runs its steps in a fixed order (requests, then penalties), a penalty for day D is charged before D's collection answer arrives. A payment on day D therefore leaves D's penalty for the next day's request.
