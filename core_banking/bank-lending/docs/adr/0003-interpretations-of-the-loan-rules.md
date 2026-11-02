# 0003. Interpretations of the loan rules

- Status: Accepted (reviewed by Majid on 2026-11-02; point 5 changed by him, the other points confirmed)
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
5. **Allocation and prepayment** (changed by Majid on 2026-11-02). Unpaid items are paid oldest due date first.
   - An item due on or before the payment date is paid penalty, then interest, then principal.
   - What is left prepays later items in due-date order. An item the money covers is paid in full. The last item it only partly covers gets the money split between its unpaid principal and unpaid interest in proportion: principal = round(money × unpaid principal / (unpaid principal + unpaid interest)), interest = the rest. Example: an item of 1,000 principal and 500 interest, 300 paid early: 200 to principal, 100 to interest.
   - No interest is waived on a prepayment, and the schedule is not recalculated. A later early payoff does not give back interest already prepaid on future items. Majid's decision for the demo scope: the customer always pays the scheduled principal and interest. A prepayment discount like the early payoff minimum interest was considered and deferred.
   - Money left after every item is paid is returned as unallocated. An amount with more decimals than the currency allows is rejected (`amount_not_in_minor_units`), so the split never pays more than is owed.
   - A partly prepaid item that falls due is treated like any other due item: unpaid the day after, with penalty after grace on what is still owed.
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
   - interest already prepaid on the next item (point 5) is subtracted from that accrual, never below 0 (Majid, 2026-11-02). Without this, a customer who prepaid would pay the earned interest twice. More prepaid than earned gives 0, not a refund.
10. **Amounts must fit the currency.** A principal, cap or stage share with more decimals than the currency's minor unit is rejected (`amount_not_in_minor_units`). Inconsistent copied product parameters are rejected as `invalid_terms` (review of 2026-10-31).

## Rationale

Each reading charges the customer no less than the rules plainly require, and gives the bank no more than they allow:

- prepaying without recalculating keeps the schedule as agreed, and the customer still owes no more than the scheduled amounts;
- treating an unpaid penalty as unpaid keeps arrears visible;
- capping the annuity accrual at one period's interest prevents a 31-day month from accruing more than the installment's own interest.

## Alternatives considered

- **Unrounded A** (each item's total varies by an öre): it matches the formula more literally, but customers would see a different amount every month. Rejected.
- **Return an overpayment instead of prepaying** (the first version, from the stage file): simplest, but Majid chose to let the customer pay ahead (2026-11-02).
- **Prepayment pays only principal, or waives interest above a minimum:** fairer to a customer who prepays and later pays off, but it needs new business rules. Deferred by Majid.
- **Reduce the principal and recalculate the remaining installments:** common in banks, but it changes the agreed schedule. Not chosen.
- **Ignore an unpaid penalty for classification:** common in some banks. Rejected as less conservative; it is a business choice for Majid to confirm.
- **Annuity accrual by actual days / days in period:** more precise for irregular months, but the stage file says days/30. Kept days/30, capped at 30.

## Consequences

- Stage 5 and the end-of-day processing call these functions exactly as interpreted here. Changing any point changes amounts, so it needs a new ADR.
- Points 5, 6 and 9 are business choices. Majid confirmed 6 and 9 and changed 5 on 2026-11-02. The tests that name them (`Apply_Overpayment_…`, `Apply_…Prepa…`, `For_OnlyPenaltyUnpaid_…`, `Quote_…`) show exactly what each one does.
- `Allocation.Apply` now takes the `LoanTerms` (for the currency's minor units in the pro rata split): `Apply(schedule, terms, amount, date)`.
