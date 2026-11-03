using Bank.Common;
using Bank.Ledger.Api.Data;
using Bank.Ledger.Api.Logic;
using Microsoft.EntityFrameworkCore;

namespace Bank.Ledger.Api.Features;

public sealed record JournalLineResponse(int GlCode, string Account, decimal Debit, decimal Credit);

public sealed record JournalEntryResponse(Guid Id, DateOnly BusinessDate, string Currency, string Description, string SourceEvent, IReadOnlyList<JournalLineResponse> Lines);

public sealed record TrialBalanceResponse(DateOnly Date, string Currency, IReadOnlyList<TrialBalanceRowResponse> Rows, decimal TotalDebit, decimal TotalCredit);

public sealed record TrialBalanceRowResponse(int GlCode, string Name, string Type, decimal Debit, decimal Credit, decimal Balance);

/// <summary>GET /journal-entries?from=&amp;to= and GET /trial-balance?date=&amp;currency= (business dates).</summary>
public static class Reports
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/journal-entries", async (DateOnly from, DateOnly to, HttpRequest http, LedgerDbContext db, CancellationToken ct) =>
        {
            if (StaffOnly.Refuse(http) is { } refused) return refused;
            var entries = await db.Entries.AsNoTracking().Include(e => e.Lines)
                .Where(e => e.BusinessDate >= from && e.BusinessDate <= to).OrderBy(e => e.BusinessDate).ThenBy(e => e.Id).ToListAsync(ct);
            return Results.Ok(entries.Select(e => new JournalEntryResponse(e.Id, e.BusinessDate, e.Currency, e.Description, e.SourceEvent,
                [.. e.Lines.OrderByDescending(l => l.Debit).ThenBy(l => l.GlCode)
                    .Select(l => new JournalLineResponse((int)l.GlCode, GlAccounts.Of(l.GlCode).Name, l.Debit, l.Credit))])));
        }).WithTags("Ledger");

        app.MapGet("/trial-balance", async (DateOnly? date, string currency, HttpRequest http, LedgerDbContext db, IBusinessClock clock, CancellationToken ct) =>
        {
            if (StaffOnly.Refuse(http) is { } refused) return refused;
            var on = date ?? clock.Today;
            var lines = await db.Lines.AsNoTracking()
                .Where(l => l.Currency == currency && db.Entries.Any(e => e.Id == l.EntryId && e.BusinessDate <= on))
                .Select(l => new JournalLine(l.GlCode, l.Debit, l.Credit))
                .ToListAsync(ct);
            var balance = TrialBalance.From(lines);
            return Results.Ok(new TrialBalanceResponse(on, currency,
                [.. balance.Rows.Select(r => new TrialBalanceRowResponse((int)r.Code, r.Name, r.Type.ToString(), r.Debit, r.Credit, r.Balance))],
                balance.TotalDebit, balance.TotalCredit));
        }).WithTags("Ledger");
    }
}
