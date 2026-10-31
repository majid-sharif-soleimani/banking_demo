using Bank.Operations.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Bank.Operations.Api.Features.Currencies;

/// <summary>GET /currencies: every currency the bank knows, active or not, ordered by code.</summary>
public static class ListCurrencies
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/currencies", HandleAsync).WithName(nameof(ListCurrencies)).WithTags("Currencies");

    private static async Task<IReadOnlyList<CurrencyResponse>> HandleAsync(OperationsDbContext db, CancellationToken ct) =>
        await db.Currencies.AsNoTracking()
            .OrderBy(c => c.Code)
            .Select(c => new CurrencyResponse(c.Code, c.MinorUnits, c.IsActive))
            .ToListAsync(ct);
}
