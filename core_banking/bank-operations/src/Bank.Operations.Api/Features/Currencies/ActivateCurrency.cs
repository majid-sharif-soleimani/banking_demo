using Bank.Common;
using Bank.Contracts.Events;
using Bank.Messaging;
using Bank.Operations.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bank.Operations.Api.Features.Currencies;

/// <summary>
/// POST /currencies/{code}/activate: makes a seeded currency usable and publishes CurrencyActivated, so every service's
/// local copy follows (ADR 0002). Activating an active currency changes nothing and publishes nothing, so the import
/// tool can call it on every run.
/// </summary>
public static class ActivateCurrency
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/currencies/{code}/activate", HandleAsync).WithName(nameof(ActivateCurrency)).WithTags("Currencies");

    private static async Task<Results<Ok<CurrencyResponse>, ProblemHttpResult>> HandleAsync(
        string code,
        [FromHeader(Name = PerformedBy.HeaderName)] string? performedBy,
        OperationsDbContext db,
        IEventPublisher publisher,
        CancellationToken ct)
    {
        var currencyCode = code.ToUpperInvariant();
        var currency = await db.Currencies.SingleOrDefaultAsync(c => c.Code == currencyCode, ct);
        if (currency is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Unknown currency",
                detail: $"The bank does not know the currency '{code}'.",
                extensions: new Dictionary<string, object?> { ["code"] = "currency_not_found" });
        }

        if (currency.IsActive)
        {
            return TypedResults.Ok(CurrencyResponse.From(currency));
        }

        var actor = PerformedBy.From(performedBy);
        currency.IsActive = true;
        currency.PerformedBy = actor;
        await publisher.PublishAsync(
            new CurrencyActivated(Guid.NewGuid(), await db.BusinessDays.CurrentDateAsync(ct), actor, currency.Code, currency.MinorUnits),
            currency.Code,
            ct);

        try
        {
            // The update carries the row's xmin: if another request activated it first, no row matches.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The other request won and published the only CurrencyActivated; our event was not saved either.
            return TypedResults.Ok(new CurrencyResponse(currency.Code, currency.MinorUnits, IsActive: true));
        }

        return TypedResults.Ok(CurrencyResponse.From(currency));
    }
}
