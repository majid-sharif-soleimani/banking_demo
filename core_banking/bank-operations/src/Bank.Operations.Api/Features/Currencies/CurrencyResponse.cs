using Bank.Operations.Api.Data;

namespace Bank.Operations.Api.Features.Currencies;

public sealed record CurrencyResponse(string Code, int MinorUnits, bool IsActive)
{
    public static CurrencyResponse From(Currency currency) => new(currency.Code, currency.MinorUnits, currency.IsActive);
}
