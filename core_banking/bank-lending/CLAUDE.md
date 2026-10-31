# bank-lending

Loans: products, applications, disbursement, schedules, payments, penalties, classification and early payoff. Stage 3 builds only the pure calculations in `src/Bank.Lending.Api/Logic/`; the database, endpoints and events come in stage 5. Plan: `docs/stages/stage-3-plan.md`.

## Logic (stage 3)

- `LoanTerms` holds every parameter (copied from the product at disbursement). Rates and percentages are fractions (6.95% = 0.0695). `MinorUnits` drives all rounding (`MoneyRounding`, AwayFromZero). No literal rate or threshold in `Logic/`.
- `ScheduleItem` is an immutable record; functions return new lists (ADR 0001). Status and unpaid amounts are computed.
- Invalid input returns `Result<T>` with a `LoanErrors` code (ADR 0002). Rule interpretations: ADR 0003.
- `Amortization.BuildAnnuity`: A rounded first; last item takes the principal difference. Due dates count from the disbursement date (or the explicit first due date), never from the previous due date.

## Gotchas

- `Penalties.ForDay` charges one day per call and is not idempotent by itself. Stage 5 must run it once per (loan, business date), with a unique key, so a replayed `DayClosed` does not charge twice.

- `DateOnly.AddMonths` clamps to the month's last day; counting from the previous due date would drift (31 Jan → 29 Feb → 29 Mar). Always count from the anchor.
- Test names follow `Method_Scenario_ExpectedResult`; hand-calculated values show the arithmetic in a comment (financial-logic rule).

## Commands

```bash
dotnet build && dotnet test                                    # no Docker needed in stage 3
dotnet test --filter "FullyQualifiedName~Amortization"
```
