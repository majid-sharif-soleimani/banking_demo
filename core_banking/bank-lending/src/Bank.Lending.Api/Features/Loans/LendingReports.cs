using Bank.Common;
using Bank.Contracts;
using Bank.Lending.Api.Data;
using Bank.Lending.Api.Logic;
using Microsoft.EntityFrameworkCore;

namespace Bank.Lending.Api.Features.Loans;

public sealed record PortfolioLine(string ProductCode, int Loans, Money Outstanding);

public sealed record PortfolioReport(DateOnly Date, IReadOnlyList<PortfolioLine> Products, int TotalLoans);

public sealed record ArrearsLine(string Classification, int Loans, Money Outstanding);

public sealed record ArrearsReport(DateOnly Date, IReadOnlyList<ArrearsLine> Classifications);

/// <summary>
/// Bank reports over Lending's own data (design plan section 2; ADR 0006), as of the end of a business date:
/// the loans running on that date are those disbursed on or before it and not closed on or before it; outstanding
/// principal = principal − principal paid by payments booked on or before it; the classification is the last change on
/// or before it (Current before any change). Staff only: customers get 403.
/// </summary>
public static class LendingReports
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/reports/portfolio", PortfolioAsync).WithTags("Reports");
        app.MapGet("/reports/arrears", ArrearsAsync).WithTags("Reports");
    }

    private sealed record Running(Guid Id, string ProductCode, string Currency, decimal Outstanding);

    private static async Task<IResult> PortfolioAsync(DateOnly? date, HttpRequest http, LendingDbContext db, IBusinessClock clock, CancellationToken ct)
    {
        if (CustomerAccess.IsCustomerCall(http))
        {
            return CustomerAccess.StaffOnly();
        }

        var on = date ?? clock.Today;
        var loans = await RunningAsync(db, on, ct);
        var lines = loans.GroupBy(l => (l.ProductCode, l.Currency)).OrderBy(g => g.Key.ProductCode)
            .Select(g => new PortfolioLine(g.Key.ProductCode, g.Count(), new Money(g.Sum(l => l.Outstanding), g.Key.Currency))).ToList();
        return Results.Ok(new PortfolioReport(on, lines, loans.Count));
    }

    private static async Task<IResult> ArrearsAsync(DateOnly? date, HttpRequest http, LendingDbContext db, IBusinessClock clock, CancellationToken ct)
    {
        if (CustomerAccess.IsCustomerCall(http))
        {
            return CustomerAccess.StaffOnly();
        }

        var on = date ?? clock.Today;
        var loans = await RunningAsync(db, on, ct);
        var ids = loans.Select(l => l.Id).ToList();
        var latest = (await db.ClassificationChanges.AsNoTracking().Where(c => ids.Contains(c.LoanId) && c.Date <= on).ToListAsync(ct))
            .GroupBy(c => c.LoanId).ToDictionary(g => g.Key, g => g.MaxBy(c => c.Date)!.ToClassification);
        var currency = loans.Select(l => l.Currency).FirstOrDefault() ?? "SEK";
        var lines = Enum.GetValues<LoanClassification>().Select(classification =>
        {
            var inClass = loans.Where(l => latest.GetValueOrDefault(l.Id, LoanClassification.Current) == classification).ToList();
            return new ArrearsLine(classification.ToString(), inClass.Count, new Money(inClass.Sum(l => l.Outstanding), currency));
        }).ToList();
        return Results.Ok(new ArrearsReport(on, lines));
    }

    private static async Task<List<Running>> RunningAsync(LendingDbContext db, DateOnly on, CancellationToken ct)
    {
        var loans = await db.Loans.AsNoTracking()
            .Where(l => l.DisbursedOn <= on && (l.ClosedOn == null || l.ClosedOn > on))
            .Select(l => new { l.Id, l.ProductCode, l.Currency, l.Principal })
            .ToListAsync(ct);
        var ids = loans.Select(l => l.Id).ToList();
        var paid = await db.Payments.AsNoTracking().Where(p => ids.Contains(p.LoanId) && p.BusinessDate <= on)
            .GroupBy(p => p.LoanId).Select(g => new { LoanId = g.Key, Principal = g.Sum(p => p.Principal) }).ToDictionaryAsync(x => x.LoanId, x => x.Principal, ct);
        return [.. loans.Select(l => new Running(l.Id, l.ProductCode, l.Currency, l.Principal - paid.GetValueOrDefault(l.Id)))];
    }
}
