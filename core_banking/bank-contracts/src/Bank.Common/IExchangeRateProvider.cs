namespace Bank.Common;

/// <summary>
/// A conversion rate: <see cref="Rate"/> units of <see cref="To"/> per unit of <see cref="From"/>, as published by
/// <see cref="Source"/> for <see cref="ObservedOn"/>. Stored on every converted transfer (design plan section 2).
/// </summary>
public sealed record ExchangeRate(string From, string To, decimal Rate, string Source, DateOnly ObservedOn);

/// <summary>
/// Exchange rates behind an interface (design plan ADR 20): the Riksbank in production, fixed rates in development and
/// tests. Throws <see cref="ExchangeRateUnavailableException"/> when no rate can be had; the caller answers fx_unavailable.
/// </summary>
public interface IExchangeRateProvider
{
    Task<ExchangeRate> GetRateAsync(string from, string to, DateOnly date, CancellationToken ct);
}

public sealed class ExchangeRateUnavailableException(string from, string to, Exception? inner = null)
    : Exception($"No exchange rate from {from} to {to} is available.", inner);
