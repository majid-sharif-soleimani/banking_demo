using Bank.Accounts.Api.Data;
using Bank.Common;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Bank.Accounts.Api.Features.Movements;

/// <summary>
/// POST /accounts/{id}/deposits and POST /accounts/{id}/withdrawals: cash in and out. Both need an Idempotency-Key and
/// run through <see cref="IdempotentCommand"/>. A withdrawal never overdraws: 422 insufficient_funds (no overdraft,
/// design plan section 2).
/// </summary>
public static class CashMovements
{
    public sealed record Request(Money Amount, string? Description);

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/accounts/{id:guid}/deposits", DepositAsync).WithName("Deposit").WithTags("Movements");
        app.MapPost("/accounts/{id:guid}/withdrawals", WithdrawAsync).WithName("Withdraw").WithTags("Movements");
    }

    private static Task<IResult> DepositAsync(
        Guid id, Request request, HttpRequest http, AccountsDbContext db, IEventPublisher publisher, IBusinessClock clock, TimeProvider time,
        CancellationToken ct) =>
        IdempotentCommand.RunAsync(http, db, $"deposit:{id}", request, async token =>
        {
            var (account, minorUnits, problem) = await LoadAsync(db, http, id, request.Amount, token);
            if (problem is not null)
            {
                return Outcome.Fail(problem);
            }

            var actor = PerformedBy.From(http.Headers[PerformedBy.HeaderName]);
            var entry = Postings.Post(db, account!, request.Amount.Amount, minorUnits, EntryKind.Deposit, null, request.Description ?? "Cash deposit",
                clock.Today, actor, time.GetUtcNow());
            await publisher.PublishAsync(
                new MoneyDeposited(Guid.NewGuid(), clock.Today, actor, account!.Id, entry.Id, new Money(entry.Amount, account.Currency), account.BalanceMoney),
                account.Id.ToString(), token);
            return Outcome.Ok(EntryResponse.From(entry, account.Currency));
        }, ct);

    private static Task<IResult> WithdrawAsync(
        Guid id, Request request, HttpRequest http, AccountsDbContext db, IEventPublisher publisher, IBusinessClock clock, TimeProvider time,
        CancellationToken ct) =>
        IdempotentCommand.RunAsync(http, db, $"withdrawal:{id}", request, async token =>
        {
            var (account, minorUnits, problem) = await LoadAsync(db, http, id, request.Amount, token);
            if (problem is not null)
            {
                return Outcome.Fail(problem);
            }

            if (account!.Balance < request.Amount.Amount)
            {
                return Outcome.Fail(InsufficientFunds(account));
            }

            var actor = PerformedBy.From(http.Headers[PerformedBy.HeaderName]);
            var entry = Postings.Post(db, account, -request.Amount.Amount, minorUnits, EntryKind.Withdrawal, null, request.Description ?? "Cash withdrawal",
                clock.Today, actor, time.GetUtcNow());
            await publisher.PublishAsync(
                new MoneyWithdrawn(Guid.NewGuid(), clock.Today, actor, account.Id, entry.Id, new Money(-entry.Amount, account.Currency), account.BalanceMoney),
                account.Id.ToString(), token);
            return Outcome.Ok(EntryResponse.From(entry, account.Currency));
        }, ct);

    public static IResult InsufficientFunds(Account account) =>
        Problems.Rule("insufficient_funds", "The account does not have enough money.", $"Balance: {account.BalanceMoney}.");

    /// <summary>The account (tracked, so the balance update carries its xmin) and its currency's minor units, or a problem.</summary>
    internal static async Task<(Account? Account, int MinorUnits, IResult? Problem)> LoadAsync(
        AccountsDbContext db, HttpRequest http, Guid id, Money? amount, CancellationToken ct)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (account is null || CustomerAccess.IsOtherCustomer(http, account.CustomerId))
        {
            return (null, 0, Problems.AccountNotFound());
        }

        var minorUnits = await MinorUnitsAsync(db, account.Currency, ct);
        return (account, minorUnits, Postings.CheckAmount(amount, account.Currency, minorUnits));
    }

    internal static Task<int> MinorUnitsAsync(AccountsDbContext db, string currency, CancellationToken ct) =>
        db.Currencies.Where(c => c.Code == currency).Select(c => c.MinorUnits).SingleAsync(ct);
}
