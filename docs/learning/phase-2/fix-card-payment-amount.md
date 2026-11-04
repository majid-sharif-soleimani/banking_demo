# Fix: the agent changed the amount of a card payment

## What we want and why

A customer asked to pay 1,000 SEK of a loan installment with a test card, and the agent prepared 8,004.17 SEK instead. Money must move by the person's numbers, not the model's. So the customer's amount now stays the customer's, and the host checks it in code.

## The building blocks

- **The instructions and the tool description** tell the model that the amount is the customer's and that a partial payment is allowed.
- **`FormCheck.Numbers`** reads the numbers from a form the person sent. `ActionEndpoints` keeps them in `HostSession.EnteredNumbers`.
- **`FormCheck.DiffersFromEntered`** compares the tool's amount with those numbers.
- **`ConfirmFirst`** calls it before it builds the card for `card_payment`. A different amount gets no card, and the model hears what the person entered and tries again.
- **Cards and Lending** did not change: they already accepted and allocated a partial payment.

## Why it is designed this way

An instruction guides a model but cannot guarantee anything, so the rule also lives in code the model cannot talk its way past. The confirmation card stays the last defence, but a person reading a long card can miss a changed number. The check does not know about loans or schedules: it only compares two numbers, which keeps the bank as the one place that decides what a payment does. Details and limits are in agent ADR 0010.
