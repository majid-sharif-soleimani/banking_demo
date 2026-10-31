# 0002. Report invalid loan input with Result<T> and error codes

- Status: Accepted (to be reviewed by Majid)
- Date: 2026-10-31
- Scope: bank-lending
- Design plan reference: `implementation-decisions.md`, "APIs" (422 with a `code`); `.claude/rules/financial-logic.md`
- Decided by: Claude on Majid's delegation of 2026-10-31. A D1 for Majid in the stage 3 file.

## Context

A schedule cannot be built for a zero principal, a negative rate, a staged plan whose shares do not sum to the principal, and so on. Someone must check that. The financial-logic rule says `Logic/` must not throw for business outcomes, and the API must answer 422 with a machine-readable `code` (`implementation-decisions.md`).

## Decision

Functions that can reject their input (`Amortization.BuildAnnuity`, `Amortization.BuildStaged`, `Allocation.Apply`) return `Result<T>` from `Bank.Common`. The failures are the shared `Error(Code, Message)` instances in `LoanErrors` (`principal_not_positive`, `too_many_stages`, …). Functions whose input is already a valid schedule (`Penalties.ForDay`, `Classification.For`, `EarlyPayoff.Quote`) return plain values. Programming errors, such as an amount in another currency, still throw (`CurrencyMismatchException` from `Money`).

## Rationale

- The rules for a valid loan live next to the calculation that needs them, and every caller (API, import tool, tests) gets the same checks.
- The error code travels unchanged to the 422 response in stage 5.
- Tests can assert on the exact code.

## Alternatives considered

- **Validation before the call** (a validator, functions assume valid input): one fewer `Result` to unwrap, but the calculation would silently produce nonsense if a caller forgot the validator. Rejected.
- **Exceptions:** against the financial-logic rule, and a rejection is an expected outcome, not an error.

## Consequences

- Callers must check `IsSuccess`; `Value` on a failure throws.
- New rejection reasons add a `LoanErrors` entry, so the codes stay documented in one file.
