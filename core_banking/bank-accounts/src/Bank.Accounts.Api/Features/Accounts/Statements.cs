using Bank.Accounts.Api.Data;
using Bank.Accounts.Api.Logic;
using Bank.Common;
using Bank.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Bank.Accounts.Api.Features.Accounts;

public sealed record StatementLine(
    Guid EntryId, DateOnly BusinessDate, string Kind, Money Amount, Money BalanceAfter, string Description, Guid? Reference);

public sealed record StatementResponse(
    Guid AccountId, string Number, DateOnly From, DateOnly To, Money OpeningBalance, IReadOnlyList<StatementLine> Lines, Money ClosingBalance);

public sealed record BalanceResponse(Guid AccountId, DateOnly Date, Money Balance);

/// <summary>
/// GET /accounts/{id}/statement?from=&amp;to= and GET /accounts/{id}/balance?date=, both read from the entries alone
/// (balance after each entry), as the design plan asks. Dates are business dates, never after today's.
/// </summary>
public static class Statements
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/accounts/{id:guid}/statement", StatementAsync).WithName("GetStatement").WithTags("Accounts");
        app.MapGet("/accounts/{id:guid}/balance", BalanceAsync).WithName("GetBalanceAtDate").WithTags("Accounts");
    }

    private static async Task<IResult> StatementAsync(
        Guid id, DateOnly from, DateOnly to, HttpRequest http, AccountsDbContext db, IBusinessClock clock, CancellationToken ct)
    {
        if (from > to)
        {
            return Problems.Of(StatusCodes.Status400BadRequest, "invalid_period", "'from' must not be after 'to'.");
        }

        var account = await FindAsync(db, http, id, ct);
        if (account is null)
        {
            return Problems.AccountNotFound();
        }

        var opening = await BalanceAtAsync(db, id, from.AddDays(-1), ct);
        var entries = await db.Entries.AsNoTracking()
            .Where(e => e.AccountId == id && e.BusinessDate >= from && e.BusinessDate <= to)
            .OrderBy(e => e.Sequence)
            .ToListAsync(ct);
        var closing = entries.Count == 0
            ? opening
            : Balances.AtDate(entries.Select(e => new BalancePoint(e.BusinessDate, e.Sequence, e.BalanceAfter)), to);

        return Results.Ok(new StatementResponse(
            id, account.Number, from, to, new Money(opening, account.Currency),
            [.. entries.Select(e => new StatementLine(e.Id, e.BusinessDate, e.Kind.ToString(), new Money(e.Amount, account.Currency),
                new Money(e.BalanceAfter, account.Currency), e.Description, e.Reference))],
            new Money(closing, account.Currency)));
    }

    private static async Task<IResult> BalanceAsync(
        Guid id, DateOnly date, HttpRequest http, AccountsDbContext db, IBusinessClock clock, CancellationToken ct)
    {
        if (date > clock.Today)
        {
            return Problems.Of(StatusCodes.Status400BadRequest, "date_in_future", "The date must not be after the current business date.");
        }

        var account = await FindAsync(db, http, id, ct);
        return account is null
            ? Problems.AccountNotFound()
            : Results.Ok(new BalanceResponse(id, date, new Money(await BalanceAtAsync(db, id, date, ct), account.Currency)));
    }

    private static async Task<Account?> FindAsync(AccountsDbContext db, HttpRequest http, Guid id, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
        return account is null || CustomerAccess.IsOtherCustomer(http, account.CustomerId) ? null : account;
    }

    /// <summary>Only the last entry on or before the date is loaded; Balances.AtDate gives it its meaning.</summary>
    private static async Task<decimal> BalanceAtAsync(AccountsDbContext db, Guid id, DateOnly date, CancellationToken ct)
    {
        var last = await db.Entries.AsNoTracking()
            .Where(e => e.AccountId == id && e.BusinessDate <= date)
            .OrderByDescending(e => e.Sequence)
            .Select(e => new BalancePoint(e.BusinessDate, e.Sequence, e.BalanceAfter))
            .Take(1)
            .ToListAsync(ct);
        return Balances.AtDate(last, date);
    }
}
