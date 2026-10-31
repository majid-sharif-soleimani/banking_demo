# 0003. Interpretations of the loan rules

- Status: Accepted (to be reviewed by Majid)
- Date: 2026-10-31
- Scope: bank-lending
- Design plan reference: section 2 (Loans); `docs/stages/stage-3.md`, "Functions and rules"
- Decided by: Claude on Majid's delegation of 2026-10-31. The stage file asks to record every rule that had to be interpreted. Where a reading was needed, the most conservative one was chosen.

## Context

The design plan and the stage file state the loan rules in one or two lines each. Writing them as code exposed questions those lines do not answer. A wrong answer moves real money, by a few öre or a whole installment, so each answer is recorded here.

## Decision

1. **Annuity payment.** A is rounded to the minor unit before the schedule is built. Items 1..n−1 total exactly A, and the last item takes the remaining principal plus its interest, so it differs from A by the accumulated rounding (CAR example: 6,170.79 instead of 6,170.85).
2. **Due dates.** Item n falls on disbursement + n months, or on the explicit first due date + (n−1) months. Dates are never counted from the previous due date, so 31 Jan gives 29 Feb, 31 Mar, 30 Apr.
3. **Staged interest** is charged on the principal outstanding *before* the stage's repayment, for the actual days since the previous stage (or disbursement), divided by 365 even in a leap year.
4. **Staged plan limits.** A stage exactly on disbursement + term is allowed. Shares must sum to the principal to the öre; no automatic adjustment.
5. **Allocation pays only items due on or before the payment date.** Future installments are never prepaid, and the rest is returned as unallocated. Within that set: oldest due date first, then penalty, interest, principal.
6. **An item is unpaid while any part (principal, interest or penalty) is unpaid.** This holds for allocation and for classification. An unpaid penalty alone keeps a loan Overdue.
7. **Penalty** is charged per item per day: round((unpaid principal + unpaid interest) × daily rate), when due date + grace days < the date. One call charges one day. Unpaid penalty never earns penalty.
8. **Days past due** = date − due date of the oldest item due on or before the date that is not fully paid. An item due today gives 0 (Current). The thresholds are inclusive: ≥ 1 is Overdue, ≥ 90 is Default.
9. **Early payoff**:
   - items due on or before the payoff date are charged in full: unpaid principal, interest and penalty;
   - the current period accrues pro rata from the last due date on or before the payoff date (or disbursement). Annuity: the next item's interest × min(days, 30)/30. Staged: Actual/365 on the principal still scheduled;
   - remaining scheduled interest = later items' interest − that accrual;
   - minimum = min(percent × original principal, cap), lowered to the remaining scheduled interest when that is smaller (then waived = 0, "no discount");
   - waived = remaining − minimum.
   - the annuity accrual counts **actual** days since the last due date, capped at 30 (review of 2026-10-31). Annuity interest itself is 30/360, so a 28-day February accrues 28/30 at most, then jumps to the full item on its due date. 30E/360 day counting would smooth this; it was not chosen, because the stage file says days/30.
   - a quote dated before disbursement accrues nothing.
10. **Amounts must fit the currency.** A principal, cap or stage share with more decimals than the currency's minor unit is rejected (`amount_not_in_minor_units`). Inconsistent copied product parameters are rejected as `invalid_terms` (review of 2026-10-31).

## Rationale

Each reading charges the customer no less than the rules plainly require, and gives the bank no more than they allow:

- returning an overpayment instead of prepaying keeps the schedule as agreed;
- treating an unpaid penalty as unpaid keeps arrears visible;
- capping the annuity accrual at one period's interest prevents a 31-day month from accruing more than the installment's own interest.

## Alternatives considered

- **Unrounded A** (each item's total varies by an öre): it matches the formula more literally, but customers would see a different amount every month. Rejected.
- **Prepay future items with an overpayment:** customer-friendly, but the stage file says overpayment is returned. Rejected.
- **Ignore an unpaid penalty for classification:** common in some banks. Rejected as less conservative; it is a business choice for Majid to confirm.
- **Annuity accrual by actual days / days in period:** more precise for irregular months, but the stage file says days/30. Kept days/30, capped at 30.

## Consequences

- Stage 5 and the end-of-day processing call these functions exactly as interpreted here. Changing any point changes amounts, so it needs a new ADR.
- Points 5, 6 and 9 are business choices. If Majid decides otherwise, the tests that name them (`Apply_Overpayment_…`, `For_OnlyPenaltyUnpaid_…`, `Quote_…`) show exactly what changes.
