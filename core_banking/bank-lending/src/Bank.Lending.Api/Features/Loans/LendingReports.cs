using Bank.Common;
using Bank.Contracts;
using Bank.Lending.Api.Data;
using Bank.Lending.Api.Logic;
using Microsoft.EntityFrameworkCore;

namespace Bank.Lending.Api.Features.Loans;

public sealed record PortfolioLine(string ProductCode, int Loans, Money Outstanding);

public sealed record PortfolioReport(DateOnly Date, IReadOnlyList<PortfolioLine> Products, int TotalLoans);

public sealed record ArrearsLine(string Classification, string Currency, int Loans, Money Outstanding);

public sealed record ArrearsReport(DateOnly Date, IReadOnlyList<ArrearsLine> Classifications);

/// <summary>
/// Bank reports (design plan section 2), as of the end of a business date. Each report reads its own projection
/// (ADR 0006, Majid 2026-11-02): the sum of its movement rows on or before the date, written by the features that change a
/// loan (<see cref="ReportProjections"/>). Staff only: customers get 403.
/// </summary>
public static class LendingReports
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/reports/portfolio", PortfolioAsync).WithTags("Reports");
        app.MapGet("/reports/arrears", ArrearsAsync).WithTags("Reports");
    }

    private static async Task<IResult> PortfolioAsync(DateOnly? date, HttpRequest http, LendingDbContext db, IBusinessClock clock, CancellationToken ct)
    {
        if (CustomerAccess.IsCustomerCall(http))
        {
            return CustomerAccess.StaffOnly();
        }

        var on = date ?? clock.Today;
        var sums = await db.PortfolioMovements.AsNoTracking().Where(m => m.BusinessDate <= on)
            .GroupBy(m => new { m.ProductCode, m.Currency })
            .Select(g => new { g.Key.ProductCode, g.Key.Currency, Loans = g.Sum(m => m.Loans), Outstanding = g.Sum(m => m.Outstanding) })
            .ToListAsync(ct);
        var lines = sums.Where(x => x.Loans > 0).OrderBy(x => x.ProductCode)
            .Select(x => new PortfolioLine(x.ProductCode, x.Loans, new Money(x.Outstanding, x.Currency))).ToList();
        return Results.Ok(new PortfolioReport(on, lines, lines.Sum(l => l.Loans)));
    }

    private static async Task<IResult> ArrearsAsync(DateOnly? date, HttpRequest http, LendingDbContext db, IBusinessClock clock, CancellationToken ct)
    {
        if (CustomerAccess.IsCustomerCall(http))
        {
            return CustomerAccess.StaffOnly();
        }

        var on = date ?? clock.Today;
        var sums = await db.ArrearsMovements.AsNoTracking().Where(m => m.BusinessDate <= on)
            .GroupBy(m => new { m.Classification, m.Currency })
            .Select(g => new { g.Key.Classification, g.Key.Currency, Loans = g.Sum(m => m.Loans), Outstanding = g.Sum(m => m.Outstanding) })
            .ToListAsync(ct);
        // Every classification for each currency with running loans; amounts in different currencies are never added
        // (review of 2026-10-31).
        var lines = (from classification in Enum.GetValues<LoanClassification>()
                     from currency in sums.Where(x => x.Loans > 0).Select(x => x.Currency).Distinct().Order()
                     let sum = sums.SingleOrDefault(x => x.Classification == classification && x.Currency == currency)
                     select new ArrearsLine(classification.ToString(), currency, sum?.Loans ?? 0, new Money(sum?.Outstanding ?? 0m, currency))).ToList();
        return Results.Ok(new ArrearsReport(on, lines));
    }
}
