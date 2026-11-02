---
paths:
  - "core_banking/**/Logic/**/*.cs"
  - "core_banking/**/*UnitTests*/**/*.cs"
---

# Financial logic rules

- Functions in `Logic/` are pure and static: inputs in, result out. No DbContext, no clock, no logging, no exceptions for business outcomes (return a result type).
- All parameters (rates, grace days, thresholds, caps) are passed in, usually as a product or loan-terms record. Never a literal in the logic.
- Rounding: to the currency's minor unit with `MidpointRounding.AwayFromZero`. Round each installment component once; put the accumulated rounding difference in the last installment.
- Annuity loans: monthly rate = annual rate / 12 (30/360). Staged loans: Actual/365 on outstanding principal from the last payment date to the stage date.
- Payment allocation order: penalty, interest, principal; oldest schedule item first. Money left prepays later items in order, split between principal and interest in proportion for a partly covered item; no interest is waived (ADR bank-lending 0003, point 5).
- Penalty: daily rate on the overdue amount, starting the day after the grace period ends (grace 5 days means penalty from day 6).
- Early payoff: outstanding principal + interest to date + minimum interest, where minimum = min(percent × original principal, cap), waived = max(0, future interest − minimum).
- Every rule above has unit tests that name the rule, plus edge cases: leap year, last-installment rounding, overpayment, zero-interest product, payoff on a due date.
- Test names: `Method_Scenario_ExpectedResult`. Use Shouldly. Hand-calculated expected values go in a comment showing the arithmetic.
