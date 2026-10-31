using System.Net.Http.Json;
using Bank.Accounts.Api.Logic;
using Bank.Common;
using Microsoft.Extensions.Options;

namespace Bank.Accounts.Api.ExchangeRates;

public sealed class ExchangeRateOptions
{
    public const string Section = "ExchangeRates";

    /// <summary>"Fixed" (development, tests) or "Riksbank" (design plan ADR 20).</summary>
    public string Provider { get; set; } = "Fixed";

    /// <summary>For the fixed provider: SEK per unit of each currency, such as EUR = 11.50 (implementation-decisions).</summary>
    public Dictionary<string, decimal> SekPerUnit { get; set; } = [];

    public Uri RiksbankBaseAddress { get; set; } = new("https://api.riksbank.se/swea/v1/");
}

/// <summary>
/// Rates from quotes against SEK, the bank's base currency: SEK → X is 1 / (SEK per X), X → SEK is (SEK per X), and
/// X → Y is crossed through SEK. Same currency is 1.
/// </summary>
public abstract class SekQuotedExchangeRateProvider : IExchangeRateProvider
{
    protected abstract string Source { get; }

    public async Task<ExchangeRate> GetRateAsync(string from, string to, DateOnly date, CancellationToken ct)
    {
        if (from == to)
        {
            return new ExchangeRate(from, to, 1m, Source, date);
        }

        var fromQuote = from == "SEK" ? (Rate: 1m, On: date) : await QuoteAsync(from, date, ct);
        var toQuote = to == "SEK" ? (Rate: 1m, On: date) : await QuoteAsync(to, date, ct);
        var rate = from == "SEK" ? Fx.Invert(toQuote.Rate)
            : to == "SEK" ? fromQuote.Rate
            : Fx.Cross(fromQuote.Rate, toQuote.Rate);
        return new ExchangeRate(from, to, rate, Source, fromQuote.On < toQuote.On ? fromQuote.On : toQuote.On);
    }

    /// <summary>SEK per one unit of <paramref name="currency"/>, and the day it was published for.</summary>
    protected abstract Task<(decimal Rate, DateOnly On)> QuoteAsync(string currency, DateOnly date, CancellationToken ct);
}

/// <summary>Fixed rates from configuration (ExchangeRates:SekPerUnit), for development and tests.</summary>
public sealed class ConfiguredExchangeRateProvider(IOptions<ExchangeRateOptions> options) : SekQuotedExchangeRateProvider
{
    protected override string Source => "fixed";

    protected override Task<(decimal Rate, DateOnly On)> QuoteAsync(string currency, DateOnly date, CancellationToken ct) =>
        options.Value.SekPerUnit.TryGetValue(currency, out var rate) && rate > 0m
            ? Task.FromResult((rate, date))
            : throw new ExchangeRateUnavailableException("SEK", currency);
}

/// <summary>
/// The Riksbank's free SWEA API: the latest published daily rate of series SEK{CURRENCY}PMI (SEK per unit), for
/// example GET Observations/Latest/SEKEURPMI → { "date": "2026-10-30", "value": 11.4995 }. Any failure means no rate:
/// the transfer then fails with fx_unavailable (implementation-decisions, External services).
/// </summary>
public sealed class RiksbankExchangeRateProvider(HttpClient http) : SekQuotedExchangeRateProvider
{
    protected override string Source => "riksbank";

    protected override async Task<(decimal Rate, DateOnly On)> QuoteAsync(string currency, DateOnly date, CancellationToken ct)
    {
        try
        {
            var observation = await http.GetFromJsonAsync<Observation>($"Observations/Latest/SEK{currency}PMI", ct);
            return observation is { Value: > 0m }
                ? (observation.Value, observation.Date)
                : throw new ExchangeRateUnavailableException("SEK", currency);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new ExchangeRateUnavailableException("SEK", currency, ex);
        }
    }

    private sealed record Observation(DateOnly Date, decimal Value);
}
