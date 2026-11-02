namespace Bank.Lending.Api.Logic;

public static class Penalties
{
    /// <summary>
    /// One day of late penalty, charged during the end of day for <paramref name="date"/>. For each item whose due date
    /// plus the grace days is before <paramref name="date"/> and that still has unpaid principal or interest:
    /// penalty += round((unpaid principal + unpaid interest) × daily rate). Grace 5 days means the first penalty day is
    /// day 6 after the due date (design plan section 2). Unpaid penalty is never part of the base.
    /// </summary>
    public static IReadOnlyList<ScheduleItem> ForDay(IReadOnlyList<ScheduleItem> schedule, LoanTerms terms, DateOnly date) =>
        [.. schedule.Select(item =>
        {
            var overdue = item.UnpaidPrincipal + item.UnpaidInterest;
            return item.DueDate.AddDays(terms.GraceDays) < date && overdue.Amount > 0m
                ? item with { PenaltyDue = item.PenaltyDue + Amortization.Round(overdue * terms.PenaltyDailyRate, terms) }
                : item;
        })];
}
