# bank-lending

Loans: products, applications, disbursement, schedules, payments, penalties, classification and early payoff. Stage 3 builds only the pure calculations in `src/Bank.Lending.Api/Logic/`; the database, endpoints and events come in stage 5. Plan: `docs/stages/stage-3-plan.md`.

## Logic (stage 3)

- `LoanTerms` holds every parameter (copied from the product at disbursement). Rates and percentages are fractions (6.95% = 0.0695). `MinorUnits` drives all rounding (`MoneyRounding`, AwayFromZero). No literal rate or threshold in `Logic/`.
- `ScheduleItem` is an immutable record; functions return new lists (ADR 0001). Status and unpaid amounts are computed.
- Invalid input returns `Result<T>` with a `LoanErrors` code (ADR 0002). Rule interpretations: ADR 0003.
- `Amortization.BuildAnnuity`: A rounded first; last item takes the principal difference. Due dates count from the disbursement date (or the explicit first due date), never from the previous due date.

## Read APIs for the agent (branch feat-agent-read-apis)

- `POST /loan-products/{code}/simulation` (`Features/Applications/LoanSimulation.cs`): installment, total interest, total cost and schedule from `Amortization` + `Logic/LoanCost`, disbursement assumed today. Stores and publishes nothing; customers and staff. Uses the same `LimitProblem`/`Terms` as submission, so limits and figures cannot drift.
- `GET /loan-applications?status=&customerId=&limit=&cursor=` (`ApplicationQueue.cs`): review queue, oldest first by (created_at, id), index `ix_loan_application_queue` (status, created_at, id). Keyset paging via `Features/Paging.cs` (workspace ADR 0004; same contract copied in customers and accounts). Staff only via `StaffAccess` (ADR 0006: 403 with X-Customer-Id, 401 without X-Performed-By).

## Gotchas

- Reports read only `portfolio_movement` and `arrears_movement` (ADR 0006). Any feature that changes a loan's principal, status or classification must call `ReportProjections` in the same `SaveChangesAsync`; call `Closed` before resetting the classification.

- `Allocation.Apply(schedule, terms, amount, date)` prepays later items once every due item is paid (pro rata principal/interest for a partly covered item, no waiver; ADR 0003 point 5, Majid 2026-11-02). `Unallocated` > 0 only when the whole loan is paid.

- `Penalties.ForDay` charges one day per call and is not idempotent by itself. Stage 5 must run it once per (loan, business date), with a unique key, so a replayed `DayClosed` does not charge twice.

- `DateOnly.AddMonths` clamps to the month's last day; counting from the previous due date would drift (31 Jan → 29 Feb → 29 Mar). Always count from the anchor.
- Test names follow `Method_Scenario_ExpectedResult`; hand-calculated values show the arithmetic in a comment (financial-logic rule).

## Commands

```bash
dotnet build && dotnet test                                    # no Docker needed in stage 3
dotnet test --filter "FullyQualifiedName~Amortization"
```
