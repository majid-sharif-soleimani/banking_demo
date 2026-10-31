using Bank.Common;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Lending.Api.Data;
using Bank.Lending.Api.Logic;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Bank.Lending.Api.Features.Loans;

public sealed record PayoffQuoteResponse(
    Guid LoanId, DateOnly Date, Money OutstandingPrincipal, Money UnpaidPenalty, Money InterestToDate, Money MinimumInterest,
    Money WaivedInterest, Money Total, Guid? CollectionId)
{
    public static PayoffQuoteResponse From(Guid loanId, DateOnly date, PayoffQuote q, Guid? collectionId = null) =>
        new(loanId, date, q.OutstandingPrincipal, q.UnpaidPenalty, q.InterestToDate, q.MinimumInterest, q.WaivedInterest, q.Total, collectionId);
}

/// <summary>
/// GET /loans/{id}/payoff-quote?date= (EarlyPayoff.Quote, stage 3) and POST /loans/{id}/payoff: quotes for today and
/// asks Accounts to debit that amount (LoanPayoffRequested). The loan settles when PaymentCollected (Payoff) arrives,
/// and stays Active when PaymentFailed does. One payoff at a time, and none while an installment collection is open,
/// so the same money can never be collected twice (ADR 0005).
/// </summary>
public static class Payoff
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/loans/{id:guid}/payoff-quote", QuoteAsync).WithTags("Loans");
        app.MapPost("/loans/{id:guid}/payoff", RequestAsync).WithTags("Loans");
    }

    private static async Task<IResult> QuoteAsync(Guid id, DateOnly? date, HttpRequest http, LendingDbContext db, IBusinessClock clock, CancellationToken ct)
    {
        var loan = await db.Loans.AsNoTracking().Include(l => l.Schedule).SingleOrDefaultAsync(l => l.Id == id, ct);
        if (loan is null || CustomerAccess.IsOtherCustomer(http, loan.CustomerId))
        {
            return LoanQueries.NotFound();
        }

        var on = date ?? clock.Today;
        if (on < clock.Today)
        {
            return Problems.Of(StatusCodes.Status400BadRequest, "date_in_past", "A payoff quote is for today or a later date.");
        }

        return loan.Status != LoanStatus.Active ? NotActive() : Results.Ok(PayoffQuoteResponse.From(loan.Id, on, Quote(loan, on)));
    }

    private static async Task<IResult> RequestAsync(
        Guid id, HttpRequest http, LendingDbContext db, IEventPublisher publisher, IBusinessClock clock, CancellationToken ct)
    {
        var loan = await db.Loans.Include(l => l.Schedule).SingleOrDefaultAsync(l => l.Id == id, ct);
        if (loan is null || CustomerAccess.IsOtherCustomer(http, loan.CustomerId))
        {
            return LoanQueries.NotFound();
        }

        if (loan.Status != LoanStatus.Active)
        {
            return NotActive();
        }

        if (loan.PendingPayoffCollectionId is not null || loan.Schedule.Any(i => i.PendingCollectionId is not null))
        {
            return Problems.Of(StatusCodes.Status409Conflict, "collection_pending", "A payment for this loan is still being collected; try again after it.");
        }

        var today = clock.Today;
        var quote = Quote(loan, today);
        var actor = PerformedBy.From(http.Headers[PerformedBy.HeaderName]);
        loan.PendingPayoffCollectionId = Guid.CreateVersion7();
        loan.PayoffPrincipal = quote.OutstandingPrincipal.Amount;
        loan.PayoffPenalty = quote.UnpaidPenalty.Amount;
        loan.PayoffInterest = (quote.InterestToDate + quote.MinimumInterest).Amount;
        loan.PayoffWaived = quote.WaivedInterest.Amount;
        loan.PerformedBy = actor;
        await publisher.PublishAsync(
            new LoanPayoffRequested(Guid.NewGuid(), today, actor, loan.Id, loan.PendingPayoffCollectionId.Value, loan.RepaymentAccountId, quote.Total),
            loan.Id.ToString(), ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.Of(StatusCodes.Status409Conflict, "collection_pending", "The loan changed meanwhile; try again.");
        }

        return Results.Accepted($"/loans/{loan.Id}", PayoffQuoteResponse.From(loan.Id, today, quote, loan.PendingPayoffCollectionId));
    }

    private static PayoffQuote Quote(Loan loan, DateOnly date) =>
        EarlyPayoff.Quote([.. loan.Schedule.OrderBy(i => i.Number).Select(i => i.ToLogic(loan.Currency))], loan.Terms, date);

    private static IResult NotActive() => Problems.Of(StatusCodes.Status409Conflict, "loan_not_active", "Only an active loan can be paid off.");
}
