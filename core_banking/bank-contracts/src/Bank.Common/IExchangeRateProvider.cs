namespace Bank.Common;

/// <summary>
/// Exchange rates for a business date: the Riksbank API in production, fixed rates in tests.
/// The rate converts one unit of <c>from</c> into <c>to</c>.
/// </summary>
public interface IExchangeRateProvider
{
    Task<decimal> GetRateAsync(string from, string to, DateOnly date, CancellationToken ct);
}
