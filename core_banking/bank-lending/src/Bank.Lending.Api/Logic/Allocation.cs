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
    /// Allocates a payment to the items due on or before <paramref name="date"/>: oldest item first, and within an item
    /// penalty, then interest, then principal (design plan section 2). Items due later are never prepaid; what is left is
    /// returned as <see cref="AllocationResult.Unallocated"/> (ADR 0003).
    /// </summary>
    public static Result<AllocationResult> Apply(IReadOnlyList<ScheduleItem> schedule, Money amount, DateOnly date)
    {
        if (amount.Amount <= 0m)
        {
            return LoanErrors.PaymentNotPositive;
        }

        if (schedule.Any(i => i.PrincipalDue.Currency != amount.Currency))
        {
            return LoanErrors.CurrencyMismatch;
        }

        var items = schedule.ToList();
        var allocations = new List<ItemAllocation>();
        var left = amount;
        foreach (var index in Enumerable.Range(0, items.Count)
                     .Where(i => items[i].DueDate <= date && !items[i].IsPaid)
                     .OrderBy(i => items[i].DueDate).ThenBy(i => items[i].Number))
        {
            if (left.Amount == 0m)
            {
                break;
            }

            var item = items[index];
            var penalty = Take(ref left, item.UnpaidPenalty);
            var interest = Take(ref left, item.UnpaidInterest);
            var principal = Take(ref left, item.UnpaidPrincipal);
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

    /// <summary>Takes up to <paramref name="owed"/> from <paramref name="left"/> and returns what was taken.</summary>
    private static Money Take(ref Money left, Money owed)
    {
        var taken = owed.Amount <= 0m ? Money.Zero(left.Currency) : left < owed ? left : owed;
        left -= taken;
        return taken;
    }
}
