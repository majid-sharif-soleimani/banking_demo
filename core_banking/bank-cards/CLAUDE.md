# bank-cards

Simulated card payments for manual loan installments (design plan section 2). Schema `cards`, topic `bank.cards.events`. Plan: `docs/stages/stage-6-plan.md`.

## Tables

- `test_card` (seeded): 9999000000000001 Approve, …0002 Decline, …0003 InsufficientFunds. Numbers starting with 9999 are never issued to real cards.
- `card_payment`: unique `idempotency_key` (+ request hash); status Captured/Declined with reason.
- `loan_ref` (from `LoanDisbursed`), `currency_ref`, `business_day`, outbox, inbox.

## Events

- Publishes `CardPaymentCaptured` (Lending allocates, source Card) and `CardPaymentDeclined` (Customers emails); key = loan id.
- Consumes `LoanDisbursed` (lending), `CurrencyActivated`, business days (operations).

## Rules in code

- `POST /card-payments`: Idempotency-Key required; card numbers normalized (digits only) and masked in responses; unknown card or loan → 422; amount in the loan currency and its minor units → otherwise 400. Answers 200 for captured and declined alike (the status says which).
- `Logic/CardDecision` maps behaviour → outcome. The card has no balance.

## Commands

```bash
dotnet build && dotnet test                         # integration tests need Docker
```
