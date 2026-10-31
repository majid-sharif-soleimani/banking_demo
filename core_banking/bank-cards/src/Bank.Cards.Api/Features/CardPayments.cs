using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bank.Cards.Api.Data;
using Bank.Cards.Api.Logic;
using Bank.Common;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bank.Cards.Api.Features;

public sealed record CardPaymentResponse(
    Guid Id, string Card, Guid LoanId, int? ScheduleItemNumber, Money Amount, string Status, string? Reason, DateOnly BusinessDate);

/// <summary>
/// POST /card-payments: a manual loan installment payment with a test card (design plan section 2). Approve publishes
/// CardPaymentCaptured (Lending allocates it); Decline and InsufficientFunds publish CardPaymentDeclined (Customers emails).
/// An Idempotency-Key is required; a repeated key returns the stored payment, the same key with another body is 422.
/// </summary>
public static class CardPayments
{
    public const string LoanInstallment = "LoanInstallment";

    public sealed record Request(string CardNumber, Money Amount, string Purpose, Guid LoanId, int? ScheduleItemNumber);

    public static void Map(IEndpointRouteBuilder app) => app.MapPost("/card-payments", PayAsync).WithTags("Card payments");

    private static async Task<IResult> PayAsync(
        Request request, HttpRequest http, CardsDbContext db, IEventPublisher publisher, IBusinessClock clock, TimeProvider time, CancellationToken ct)
    {
        var key = http.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
        {
            return Problem(StatusCodes.Status400BadRequest, "idempotency_key_required", "An Idempotency-Key header (at most 100 characters) is required.");
        }

        var cardNumber = CardDecision.Normalize(request.CardNumber ?? "");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request with { CardNumber = cardNumber }, ContractJson.Options))));
        if (await ReplayAsync(db, key, hash, ct) is { } replay)
        {
            return replay;
        }

        if (request.Purpose != LoanInstallment)
        {
            return Problem(StatusCodes.Status400BadRequest, "invalid_purpose", $"The purpose must be {LoanInstallment}.");
        }

        var card = await db.Cards.AsNoTracking().SingleOrDefaultAsync(c => c.Number == cardNumber, ct);
        if (card is null)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "unknown_card", "This card is not one of the test cards.");
        }

        var loan = await db.Loans.AsNoTracking().SingleOrDefaultAsync(l => l.LoanId == request.LoanId, ct);
        if (loan is null || IsOtherCustomer(http, loan.CustomerId))
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "loan_not_found", "No such loan for this customer.");
        }

        var minorUnits = await db.Currencies.Where(c => c.Code == loan.Currency).Select(c => (int?)c.MinorUnits).SingleOrDefaultAsync(ct);
        if (minorUnits is null)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "currency_not_active", $"{loan.Currency} is not known to Cards yet.");
        }

        if (request.Amount is null || request.Amount.Currency != loan.Currency || request.Amount.Amount <= 0m
            || Math.Round(request.Amount.Amount, minorUnits.Value) != request.Amount.Amount)
        {
            return Problem(StatusCodes.Status400BadRequest, "invalid_amount", $"A positive amount in {loan.Currency} with at most {minorUnits} decimals is required.");
        }

        var decision = CardDecision.For(card.Behavior);
        var actor = PerformedBy.From(http.Headers[PerformedBy.HeaderName]);
        var payment = new CardPayment
        {
            Id = Guid.CreateVersion7(), CardNumber = cardNumber, LoanId = loan.LoanId, ScheduleItemNumber = request.ScheduleItemNumber,
            Amount = request.Amount.Amount, Currency = request.Amount.Currency, Purpose = request.Purpose,
            Status = decision.Captured ? "Captured" : "Declined", Reason = decision.Reason, IdempotencyKey = key, RequestHash = hash,
            BusinessDate = clock.Today, CreatedAt = time.GetUtcNow(), PerformedBy = actor,
        };
        db.Payments.Add(payment);
        if (decision.Captured)
        {
            await publisher.PublishAsync(
                new CardPaymentCaptured(Guid.NewGuid(), clock.Today, actor, payment.Id, loan.LoanId, request.ScheduleItemNumber, request.Amount), loan.LoanId.ToString(), ct);
        }
        else
        {
            await publisher.PublishAsync(
                new CardPaymentDeclined(Guid.NewGuid(), clock.Today, actor, payment.Id, loan.LoanId, loan.CustomerId, request.Amount, decision.Reason!), loan.LoanId.ToString(), ct);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: CardsDbContext.IdempotencyIndex })
        {
            db.ChangeTracker.Clear();
            return await ReplayAsync(db, key, hash, ct) ?? throw new InvalidOperationException("The card payment vanished after a key conflict.");
        }

        return Respond(payment);
    }

    private static async Task<IResult?> ReplayAsync(CardsDbContext db, string key, string hash, CancellationToken ct)
    {
        var existing = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.IdempotencyKey == key, ct);
        return existing is null ? null
            : existing.RequestHash != hash ? Problem(StatusCodes.Status422UnprocessableEntity, "idempotency_key_reused", "This Idempotency-Key was used for another payment.")
            : Respond(existing);
    }

    /// <summary>200 whether captured or declined: the request was processed; the status says what the card network answered.</summary>
    private static IResult Respond(CardPayment p) => Results.Ok(new CardPaymentResponse(
        p.Id, CardDecision.Mask(p.CardNumber), p.LoanId, p.ScheduleItemNumber, new Money(p.Amount, p.Currency), p.Status, p.Reason, p.BusinessDate));

    private static bool IsOtherCustomer(HttpRequest request, Guid ownerId) =>
        request.Headers.TryGetValue("X-Customer-Id", out var value) && (!Guid.TryParse(value, out var caller) || caller != ownerId);

    private static IResult Problem(int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}

/// <summary>loan_ref from LoanDisbursed, currency_ref from CurrencyActivated. Idempotent. Class names are inbox keys.</summary>
public sealed class LoanDisbursedHandler(CardsDbContext db, TimeProvider time) : IEventHandler<LoanDisbursed>
{
    public async Task HandleAsync(LoanDisbursed e, CancellationToken ct)
    {
        if (!await db.Loans.AnyAsync(l => l.LoanId == e.LoanId, ct))
        {
            db.Loans.Add(new LoanRef { LoanId = e.LoanId, CustomerId = e.CustomerId, Currency = e.Principal.Currency, CreatedAt = time.GetUtcNow(), PerformedBy = e.PerformedBy });
        }
    }
}

public sealed class CurrencyActivatedHandler(CardsDbContext db, TimeProvider time) : IEventHandler<CurrencyActivated>
{
    public async Task HandleAsync(CurrencyActivated e, CancellationToken ct)
    {
        if (!await db.Currencies.AnyAsync(c => c.Code == e.Code, ct))
        {
            db.Currencies.Add(new CurrencyRef { Code = e.Code, MinorUnits = e.MinorUnits, CreatedAt = time.GetUtcNow(), PerformedBy = e.PerformedBy });
        }
    }
}
