using Bank.Common;
using Bank.Contracts;

namespace Bank.Lending.Api.Logic;

/// <summary>What one payment paid on one item, for the ledger (interest income versus principal) in later stages.</summary>
public sealed record ItemAllocation(int Number, Money Penalty, Money Interest, Money Principal)
{
    public Money Total => Penalty + Interest + Principal;
}

/// <summary>The schedule after the payment, what was paid where, and what could not be allocated (returned to the payer).</summary>
public sealed record AllocationResult(IReadOnlyList<ScheduleItem> Schedule, IReadOnlyList<ItemAllocation> Allocations, Money Unallocated);

public static class Allocation
{
    /// <summary>
    /// Allocates a payment, oldest item first (design plan section 2). Items due on or before <paramref name="date"/> are paid
    /// penalty, then interest, then principal. What is left prepays later items in due-date order: an item the money covers
    /// is paid in full, and the last one it only partly covers gets the money split between its unpaid principal and unpaid
    /// interest in proportion. No interest is waived on a prepayment. Money left after every item is paid is returned as
    /// <see cref="AllocationResult.Unallocated"/> (Majid, 2026-11-02, ADR 0003 point 5).
    /// </summary>
    public static Result<AllocationResult> Apply(IReadOnlyList<ScheduleItem> schedule, LoanTerms terms, Money amount, DateOnly date)
    {
        if (amount.Amount <= 0m)
        {
            return LoanErrors.PaymentNotPositive;
        }

        if (schedule.Any(i => i.PrincipalDue.Currency != amount.Currency))
        {
            return LoanErrors.CurrencyMismatch;
        }

        // The pro rata split relies on whole minor units: 3.992 could otherwise pay 1.002 on 1.00 of interest.
        if (Amortization.Round(amount, terms) != amount)
        {
            return LoanErrors.AmountNotInMinorUnits;
        }

        var items = schedule.ToList();
        var allocations = new List<ItemAllocation>();
        var left = amount;
        foreach (var index in Enumerable.Range(0, items.Count)
                     .Where(i => !items[i].IsPaid)
                     .OrderBy(i => items[i].DueDate).ThenBy(i => items[i].Number))
        {
            if (left.Amount == 0m)
            {
                break;
            }

            var item = items[index];
            var (penalty, interest, principal) = item.DueDate <= date || left >= item.Unpaid
                ? (Take(ref left, item.UnpaidPenalty), Take(ref left, item.UnpaidInterest), Take(ref left, item.UnpaidPrincipal))
                : ProRata(ref left, item, terms);
            items[index] = item with
            {
                PaidPenalty = item.PaidPenalty + penalty,
                PaidInterest = item.PaidInterest + interest,
                PaidPrincipal = item.PaidPrincipal + principal,
            };
            allocations.Add(new ItemAllocation(item.Number, penalty, interest, principal));
        }

        return new AllocationResult(items, allocations, left);
    }

    /// <summary>
    /// Splits all of <paramref name="left"/> (less than the item's unpaid total) between the future item's unpaid principal and
    /// unpaid interest in proportion; principal is rounded and interest takes the rest. A future item has no penalty yet.
    /// </summary>
    private static (Money Penalty, Money Interest, Money Principal) ProRata(ref Money left, ScheduleItem item, LoanTerms terms)
    {
        var owed = item.UnpaidPrincipal + item.UnpaidInterest;
        var principal = Amortization.Round(left * (item.UnpaidPrincipal.Amount / owed.Amount), terms);
        var interest = left - principal;
        left = Money.Zero(left.Currency);
        return (Money.Zero(left.Currency), interest, principal);
    }

    /// <summary>Takes up to <paramref name="owed"/> from <paramref name="left"/> and returns what was taken.</summary>
    private static Money Take(ref Money left, Money owed)
    {
        var taken = owed.Amount <= 0m ? Money.Zero(left.Currency) : left < owed ? left : owed;
        left -= taken;
        return taken;
    }
}
