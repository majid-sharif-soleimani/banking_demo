using Microsoft.EntityFrameworkCore;

namespace Bank.Operations.Api.Data;

public static class BusinessDayQueries
{
    /// <summary>
    /// The current business day: the one that is not Closed yet (Open, or Closing while the participants run their
    /// end-of-day work). Null only before the first day was opened at startup.
    /// </summary>
    public static Task<BusinessDay?> CurrentAsync(this IQueryable<BusinessDay> days, CancellationToken ct) =>
        days.Where(d => d.Status != BusinessDayStatus.Closed).OrderByDescending(d => d.Date).FirstOrDefaultAsync(ct);

    /// <summary>The current business date; Operations' own business clock, read from its own table (design plan ADR 14).</summary>
    public static async Task<DateOnly> CurrentDateAsync(this IQueryable<BusinessDay> days, CancellationToken ct) =>
        (await days.CurrentAsync(ct))?.Date
        ?? throw new InvalidOperationException("No business day is open. Operations opens Operations:StartDate at startup.");
}
