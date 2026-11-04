# 0010. The customer chooses the card payment amount

- Status: Accepted
- Date: 2026-11-04
- Scope: agent (Bank.Agent.Host, Bank.Agent.Mcp), design plan section 2 (Manual payment)
- Design plan reference: `phase1-design-plan.md` section 2; `phase2-design-plan.md` sections 2.2 and 4.3
- Decided by: Majid (partial payments are allowed, and the fix below), 2026-11-04.

## Context

A customer entered 1,000 SEK in the payment form twice. The model refused it, said the amount must match the schedule, and prepared 8,004.17 SEK instead. The customer pressed Confirm and the bank captured that amount. No rule of the bank asks for this: Cards accepts any positive amount, and Lending allocates a partial payment. The confirmation card showed the real amount, as designed, but nothing compared it with what the customer had typed. The tool description also told the model to take the amount from `loans_list`.

## Decision

1. A customer may pay less than the installment. The bank takes penalty, then interest, then principal, and the rest stays due.
2. The model's instructions and the `card_payment` description say that the amount is the customer's. The model may point out a difference; it may not replace the amount.
3. The host enforces it. When a form is sent, the session keeps its numbers. `ConfirmFirst` refuses a `card_payment` whose amount is not one of them, so no card appears and the model is told the entered amount. Building a card uses the numbers up, so a later payment starts fresh.

## Consequences

- To change an entered amount, the model has to ask with a new form.
- The check covers amounts typed in a `number` field of a form. An amount written in a chat message, or in a `text` field, is protected only by the instructions and by the card the person reads.
- The check covers `card_payment` only. Transfers and standing orders have the instruction, not the check.
- With an installment number, money above that installment stays unallocated and is owed back. The tool description tells the model to leave the number out when the amount is larger.
- A person's button message ("I sent the form.") no longer sets the answer language; this came up in the same chat.
