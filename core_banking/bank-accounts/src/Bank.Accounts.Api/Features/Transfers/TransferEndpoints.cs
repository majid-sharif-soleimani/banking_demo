using Bank.Accounts.Api.Data;
using Bank.Accounts.Api.Features.Movements;
using Bank.Common;
using Bank.Contracts;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Bank.Accounts.Api.Features.Transfers;

public sealed record TransferResponse(
    Guid TransferId, Guid FromAccountId, Guid ToAccountId, Money Amount, Money ConvertedAmount, decimal? FxRate, string? FxSource,
    DateOnly? FxObservedOn, DateOnly BusinessDate)
{
    public static TransferResponse From(Transfer t) =>
        new(t.Id, t.FromAccountId, t.ToAccountId, t.AmountMoney, t.ConvertedMoney, t.FxRate, t.FxSource, t.FxObservedOn, t.BusinessDate);
}

/// <summary>
/// POST /transfers: from an account (owned by the caller when X-Customer-Id is set) to any account in the bank, in the
/// source currency, converted on the spot when the target currency differs. Idempotency-Key required.
/// </summary>
public static class TransferEndpoints
{
    public sealed record Request(Guid FromAccountId, Guid ToAccountId, Money Amount, string? Description);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/transfers", HandleAsync).WithName("Transfer").WithTags("Transfers");

    private static Task<IResult> HandleAsync(
        Request request, HttpRequest http, AccountsDbContext db, IEventPublisher publisher, IExchangeRateProvider rates,
        IBusinessClock clock, TimeProvider time, CancellationToken ct) =>
        IdempotentCommand.RunAsync(http, db, $"transfer:{request.FromAccountId}", request, async token =>
        {
            var (from, _, problem) = await CashMovements.LoadAsync(db, http, request.FromAccountId, request.Amount, token);
            if (problem is not null)
            {
                return Outcome.Fail(problem);
            }

            var to = await db.Accounts.SingleOrDefaultAsync(a => a.Id == request.ToAccountId, token);
            if (to is null)
            {
                return Outcome.Fail(Problems.Rule("target_account_not_found", "The target account does not exist."));
            }

            var actor = PerformedBy.From(http.Headers[PerformedBy.HeaderName]);
            var (transfer, failure) = await TransferMoney.ExecuteAsync(
                db, publisher, rates, time, from!, to, request.Amount, clock.Today, actor, null, request.Description ?? "Transfer", token);
            return failure switch
            {
                TransferFailure.None => Outcome.Ok(TransferResponse.From(transfer!)),
                TransferFailure.SameAccount => Outcome.Fail(Problems.Rule("same_account", "The source and target accounts are the same.")),
                TransferFailure.InsufficientFunds => Outcome.Fail(CashMovements.InsufficientFunds(from!)),
                TransferFailure.FxUnavailable => Outcome.Fail(Problems.Rule("fx_unavailable", "No exchange rate is available right now; try again later.")),
                _ => Outcome.Fail(Problems.Rule("amount_too_small", "The converted amount rounds to zero.")),
            };
        }, ct);
}
