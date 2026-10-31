namespace Bank.Accounts.Api.Logic;

public enum Frequency
{
    Weekly,
    Monthly,
    Quarterly,
}

/// <summary>When a standing order runs: from its start date, every week, month or quarter, until the optional end date.</summary>
public sealed record OrderSchedule(Frequency Frequency, DateOnly StartDate, DateOnly? EndDate);

public static class StandingOrders
{
    public static DateOnly FirstRunDate(OrderSchedule schedule) => schedule.StartDate;

    /// <summary>
    /// The first scheduled run strictly after <paramref name="after"/>, or null when it would fall after the end date.
    /// Run n is always counted from the start date (StartDate + n weeks, n months or 3n months), so an order on the
    /// 31st runs on the last day of shorter months and returns to the 31st afterwards (implementation-decisions).
    /// </summary>
    public static DateOnly? NextRunDate(OrderSchedule schedule, DateOnly after)
    {
        for (var n = 0; ; n++)
        {
            var run = Run(schedule, n);
            if (run > after)
            {
                return schedule.EndDate is { } end && run > end ? null : run;
            }
        }
    }

    private static DateOnly Run(OrderSchedule schedule, int n) => schedule.Frequency switch
    {
        Frequency.Weekly => schedule.StartDate.AddDays(7 * n),
        Frequency.Monthly => schedule.StartDate.AddMonths(n),
        Frequency.Quarterly => schedule.StartDate.AddMonths(3 * n),
        _ => throw new ArgumentOutOfRangeException(nameof(schedule), schedule.Frequency, "Unknown frequency."),
    };
}
