# Stage 3 plan: Lending pure logic (TDD)

## Context

Stage 2 is Ready for review (not merged, see `docs/progress.md`). Stage 3 writes every loan calculation as a pure, fully tested function in `core_banking/bank-lending/src/Bank.Lending.Api/Logic/`, before Lending has a database, endpoints or events (stage 5). The functions take the loan's terms and schedule as values and return new values: no clock, no I/O, no exceptions for business outcomes (`.claude/rules/financial-logic.md`). The hard part is not the formulas but their edges: rounding to the cent with the difference in the last installment, month ends and leap years, the grace period boundary, the classification thresholds, and early payoff when little interest remains.

Environment: the main folder on Majid's computer, branch `stage-3-lending-logic` (from `stage-2-operations`).

**Working mode (Majid, 2026-10-31):** Claude writes everything, including the tests the stage file assigns to Majid, test first, with a learning document per slice. D1 decisions are made by Claude under Majid's delegation and listed under "Decisions to review". No questions to Majid during the run.

## Slices

### 3.1 Types and annuity
- **Folders:** `core_banking/bank-lending/` with `Directory.Build.props`, `Directory.Packages.props` (`Bank.Contracts`, `Bank.Common` 0.2.0), `nuget.config`, `CLAUDE.md`, `docs/adr/`, solution; `src/Bank.Lending.Api` (placeholder `Program.cs`, `Logic/`); `tests/Bank.Lending.UnitTests`; `.github/workflows/bank-lending.yml`.
- **Types:** `LoanTerms` (+ `MinorUnits`, `MaxStages`, see D2), `RepaymentType`, `Stage`, `StagePlan`, `ScheduleItem` (status computed from the amounts), `LoanErrors`.
- **Function:** `Amortization.BuildAnnuity(terms) → Result<IReadOnlyList<ScheduleItem>>`.
- **Tests first:** CAR and MORTGAGE worked examples, zero rate, one-month term, last-installment rounding, month-end due dates, invalid input codes.

### 3.2 Staged loans
- **Function:** `Amortization.BuildStaged(terms, plan) → Result<IReadOnlyList<ScheduleItem>>`.
- **Tests first:** PURCHASE worked example, Feb 29 inside a stage (still /365), every rejection (too many stages, shares not summing, dates not increasing, past the term, non-positive share, currency).

### 3.3 Allocation and penalties
- **Functions:** `Allocation.Apply(schedule, amount, date) → Result<AllocationResult>`, `Penalties.ForDay(schedule, terms, date) → IReadOnlyList<ScheduleItem>`.
- **Tests first:** payment exactly equal to an item, partial, overpayment prepays later items (pro rata when partly covered, after the 2026-11-02 review), payment on the due date, penalty, interest and principal order, oldest first; grace boundary (no penalty on day 5, penalty on day 6), payment during grace, no penalty on penalty, rounding.

### 3.4 Classification and early payoff
- **Functions:** `Classification.For(schedule, terms, date) → LoanClassification`, `EarlyPayoff.Quote(schedule, terms, date) → PayoffQuote`.
- **Tests first:** thresholds at exactly 1 and 90 days, payoff on a due date, mid-period pro rata (annuity days/30, staged Actual/365), remaining interest below the minimum, cap versus percentage.

### 3.5 Property-based invariants
- Theories over many generated loans (fixed seed): schedule principal sums to P; every amount non-negative; allocation never makes paid > due and conserves money; penalties never decrease; payoff total ≥ outstanding principal; waived ≥ 0.

## Decisions to review (Majid's delegation of 2026-10-31)

| Decision (D1 in the stage file) | ADR | Chosen | Main alternative |
| --- | --- | --- | --- |
| Representation of the schedule | bank-lending 0001 | Immutable records; every function returns a new list | Mutable items changed in place |
| How invalid input is reported | bank-lending 0002 | `Result<T>` with error codes from `LoanErrors` | Validation before the call, functions assume valid input |
| Interpretations of the rules (the stage file asks to record them) | bank-lending 0003 | See the ADR; the main points below | — |

Interpretations (ADR 0003), each the most conservative reading except point 5, which Majid changed on 2026-11-02:

- **Allocation and prepayment** (changed by Majid on 2026-11-02): due items are paid first; the rest prepays later items in order, pro rata between principal and interest for a partly covered item, no interest waived (ADR 0003 point 5). The first version, the most conservative reading, paid only items due by the payment date and returned the rest.
- **An item is unpaid while any of principal, interest or penalty is unpaid**, for allocation and for classification. An unpaid penalty alone therefore keeps a loan Overdue.
- **Days past due** come from the oldest unpaid item due before the date. An item due today is not yet past due (0 days, Current).
- **Early payoff:** items due on or before the payoff date are charged in full (unpaid interest and penalty). The current period's interest is accrued pro rata (annuity: min(days, 30)/30 of the next item's interest; staged: Actual/365 on outstanding principal since the last due date or disbursement). "Remaining scheduled interest" is the interest of later items minus what was just accrued. Minimum = min(percent × original principal, cap), lowered to the remaining scheduled interest when that is smaller.
- **Annuity payment A is rounded** to the currency's minor unit before the schedule is built; the last item takes the principal difference, so its total differs from A by a few cents.

## ADRs this stage needs

bank-lending 0001, 0002, 0003 above, all written by Claude, marked for Majid's review.

## Blocking questions

None. Gaps are resolved conservatively and listed above.

## Close-out (2026-10-31)

- Done-when: `dotnet build` (0 warnings) and `dotnet test`: 1,491 passed (91 examples, 1,400 invariant cases).
- `banking-reviewer`: no logic defect, and every hand-calculated value was independently confirmed. REQUIRED, fixed: worked examples per seed product for allocation (MORTGAGE, PURCHASE), classification (MORTGAGE, PURCHASE) and payoff (MORTGAGE). OPTIONAL, applied: a payoff before disbursement accrues nothing; amounts must fit the minor units; copied product parameters are sanity-checked; zero-rate and long-first-period payoff tests; the actual-days annuity accrual is recorded in ADR 0003; a penalty idempotency note for stage 5.
- To review: ADRs bank-lending 0001–0003 (D1 on delegation; ADR 0003 points 5, 6 and 9 are business choices). Done 2026-11-02, see the sign-off below.
- `/retro 3` needs Majid: pending. Not merged; stage 4 continues on `stage-4-customers-accounts`.

## Done when

```bash
cd core_banking/bank-lending && dotnet build && dotnet test
```

## Sign-off (2026-11-02)

Majid reviewed the stage decisions:

- ADR 0001 (immutable schedule records) and ADR 0002 (`Result<T>` with error codes): confirmed.
- ADR 0003 points 6 (an unpaid penalty keeps the item unpaid) and 9 (early payoff accrual and minimum): confirmed.
- ADR 0003 point 5 changed: an overpayment prepays later items in order instead of being returned. A partly covered later item gets the money split between principal and interest in proportion. No interest is waived, the schedule is not recalculated, and only money left after every item is paid is returned. `Allocation.Apply` now takes `LoanTerms` for the rounding. 1,494 tests green.
- `banking-reviewer` on this change: REQUIRED 1 fixed, an amount with too many decimals is rejected (`amount_not_in_minor_units`). REQUIRED 2 decided by Majid: the early payoff accrual subtracts interest already prepaid on the next item, never below 0 (ADR 0003 point 9). 1,498 tests green. OPTIONAL, not applied: stronger allocation invariants (amounts up to 1.2 × unpaid, prepayment order), a penalty guard in the pro rata split, and a prepaid-interest flag for the stage 6 ledger (unearned income).
- Follow-up for stage 5 (branch `stage-5-lending-service`): the payment handler calls `Allocation.Apply` with the old signature and documents "never prepaid", so it must be updated when stage 5 picks up this change.

