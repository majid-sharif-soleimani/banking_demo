using System.Net;
using Bank.Accounts.Api.ExchangeRates;
using Bank.Common;

namespace Bank.Accounts.IntegrationTests;

/// <summary>The Riksbank provider against a stubbed HTTP handler: no network in tests.</summary>
public class RiksbankProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_latest_observation_becomes_the_rate_with_its_date()
    {
        var provider = Provider(path => path.EndsWith("SEKEURPMI") ? """{"date":"2026-02-27","value":11.4995}""" : null);

        var rate = await provider.GetRateAsync("EUR", "SEK", new DateOnly(2026, 3, 1), Ct);

        rate.ShouldBe(new ExchangeRate("EUR", "SEK", 11.4995m, "riksbank", new DateOnly(2026, 2, 27)));
    }

    [Fact]
    public async Task Sek_to_foreign_is_inverted()
    {
        var provider = Provider(_ => """{"date":"2026-02-27","value":10.00}""");

        (await provider.GetRateAsync("SEK", "USD", new DateOnly(2026, 3, 1), Ct)).Rate.ShouldBe(0.1m);
    }

    [Fact]
    public async Task Two_foreign_currencies_are_crossed()
    {
        var provider = Provider(path => path.EndsWith("SEKEURPMI") ? """{"date":"2026-02-27","value":11.50}""" : """{"date":"2026-02-26","value":10.60}""");

        var rate = await provider.GetRateAsync("EUR", "USD", new DateOnly(2026, 3, 1), Ct);

        rate.Rate.ShouldBe(1.0849056604m);
        rate.ObservedOn.ShouldBe(new DateOnly(2026, 2, 26)); // the older of the two quotes
    }

    [Theory]
    [InlineData(null)]                                // 404
    [InlineData("not json")]
    [InlineData("""{"date":"2026-02-27","value":0}""")]
    public async Task Any_failure_means_no_rate(string? body)
    {
        var provider = Provider(_ => body);

        await Should.ThrowAsync<ExchangeRateUnavailableException>(() => provider.GetRateAsync("EUR", "SEK", new DateOnly(2026, 3, 1), Ct));
    }

    private static RiksbankExchangeRateProvider Provider(Func<string, string?> respond) =>
        new(new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("https://api.riksbank.se/swea/v1/") });

    private sealed class StubHandler(Func<string, string?> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = respond(request.RequestUri!.AbsolutePath);
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
