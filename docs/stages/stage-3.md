# Stage 3: Lending pure logic (TDD)

Goal: every loan calculation exists as pure, fully tested functions, before any Lending endpoint or database exists.

Read first: design plan section 2 (Loans) and `.claude/rules/financial-logic.md`. Seed products in `docs/implementation-decisions.md`.

## Learning

| Concepts | Decisions for Majid (D1) | Majid writes |
| --- | --- | --- |
| TDD (red, green, refactor), pure functions ("functional core, imperative shell"), property-based tests and invariants, result types instead of exceptions | Representation of the schedule (immutable records returned as new lists, or mutable items); how invalid input is reported (`Result<T>` with error codes, or validation before the call) | All the tests in this stage first (they are the specification), then `Amortization.BuildAnnuity`, `Allocation.Apply` and `Penalties.ForDay` |

Claude implements `Amortization.BuildStaged`, `Classification.For` and `EarlyPayoff.Quote` against Majid's tests, and Majid reviews that code. This is the stage closest to Majid's banking experience, so the focus is on test design rather than on the formulas.

Slices: (1) types and annuity, (2) staged loans, (3) allocation and penalties, (4) classification and early payoff, (5) property-based invariants.

## Folder created

`core_banking/bank-lending` with `src/Bank.Lending.Api/Logic/` and `tests/Bank.Lending.UnitTests/` only. `Program.cs` may be a minimal placeholder; no database, no endpoints yet.

## Types (in Logic/)

- `LoanTerms`: principal (Money), annual rate, term months, repayment type, penalty daily rate, grace days, overdue-after days, default-after days, payoff min percent, payoff cap, first due date (default: disbursement date + 1 month, same day of month), disbursed on.
- `StagePlan`: list of (due date, principal share as Money). Shares must sum to the principal.
- `ScheduleItem`: number, due date, principal due, interest due, penalty due, paid principal, paid interest, paid penalty, status.
- `AllocationResult`, `PayoffQuote`, `Classification` (Current, Overdue, Default).

## Functions and rules

| Function | Rule |
| --- | --- |
| `Amortization.BuildAnnuity(terms)` | Monthly payment A = P·r(1+r)^n/((1+r)^n−1), r = annual/12. Each item: interest = round(outstanding·r), principal = A − interest. Last item takes the rounding difference so principal sums exactly to P. Due dates: first due date, then same day each month (last day of month if shorter). |
| `Amortization.BuildStaged(terms, plan)` | Each stage: principal = share; interest = round(outstanding·rate·days/365), days from previous due date (or disbursement) to this due date. Leap years still use 365. Rejects plans with more stages than max, shares not summing to principal, dates not increasing, or past the term. |
| `Allocation.Apply(schedule, amount, date)` | Oldest unpaid item first; within an item: penalty, interest, principal. Returns updated items and any unallocated remainder (overpayment is returned, not applied to future items). |
| `Penalties.ForDay(schedule, terms, date)` | For each item with unpaid amount whose due date + grace days < date: penalty += round((unpaid principal + unpaid interest) × daily rate). Penalty is not charged on penalty. |
| `Classification.For(schedule, terms, date)` | Days past due = date − oldest unpaid due date. 0 → Current; ≥ overdue-after → Overdue; ≥ default-after → Default. |
| `EarlyPayoff.Quote(schedule, terms, date)` | Outstanding principal + unpaid penalty + interest accrued to date (annuity: current period interest pro rata by days/30; staged: Actual/365 since last due date) + minimum interest. Minimum = min(percent × original principal, cap). Waived = max(0, remaining scheduled interest − minimum). If remaining scheduled interest < minimum, minimum = remaining scheduled interest. |

## Tests (minimum)

For each function: one worked example per seed product with the arithmetic in a comment, plus edge cases: zero rate, one-month term, leap year (Feb 29 inside a stage), last-installment rounding, payment exactly equal to an item, partial payment, overpayment, payment on due date, payment during grace, first penalty day (grace 5 → day 6), classification thresholds at exactly 1 and 90 days, payoff on a due date, payoff when remaining interest is below the minimum.

Use property-style tests (xUnit theories over many inputs) for invariants: principal of the schedule sums to P; allocation never makes paid > due; payoff total ≥ outstanding principal.

## Done when

```bash
cd core_banking/bank-lending && dotnet build && dotnet test
```

No ADRs needed unless a rule above had to be interpreted; then record the interpretation.
