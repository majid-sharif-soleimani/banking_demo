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

    /// <summary>
    /// The current business day, locked (FOR UPDATE) until the caller's transaction ends: a second caller waits here
    /// and then sees everything the first one committed (ADR 0001). Needs an open transaction, such as a handler's.
    /// </summary>
    public static Task<BusinessDay?> CurrentForUpdateAsync(this DbSet<BusinessDay> days, CancellationToken ct) =>
        // LINQ has no FOR UPDATE. xmin is a system column, so SELECT * leaves it out; the Version property needs it named.
        days.FromSql($"SELECT *, xmin FROM business_day WHERE status <> 'Closed' ORDER BY date DESC LIMIT 1 FOR UPDATE")
            .SingleOrDefaultAsync(ct);

    /// <summary>The current business date; Operations' own business clock, read from its own table (design plan ADR 14).</summary>
    public static async Task<DateOnly> CurrentDateAsync(this IQueryable<BusinessDay> days, CancellationToken ct) =>
        (await days.CurrentAsync(ct))?.Date
        ?? throw new InvalidOperationException("No business day is open. Operations opens Operations:StartDate at startup.");
}
