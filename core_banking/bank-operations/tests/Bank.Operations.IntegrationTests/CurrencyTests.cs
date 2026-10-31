using System.Net;
using System.Net.Http.Json;
using Bank.Contracts.Events;
using Bank.Operations.Api.Features.Currencies;
using Bank.Operations.IntegrationTests.Support;
using Bank.Testing;
using Microsoft.AspNetCore.Mvc;

namespace Bank.Operations.IntegrationTests;

public class CurrencyTests(PostgresFixture postgres)
{
    private static readonly DateOnly Today = new(2026, 1, 1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_returns_the_seeded_currencies_inactive()
    {
        await using var app = await StartWithOpenDayAsync();

        var currencies = await app.GetJsonAsync<CurrencyResponse[]>("/currencies");

        currencies.ShouldBe([new("EUR", 2, false), new("SEK", 2, false), new("USD", 2, false)]);
    }

    [Fact]
    public async Task Activating_publishes_CurrencyActivated_once_with_the_business_date_and_actor()
    {
        await using var app = await StartWithOpenDayAsync();

        var response = await app.PostAsync("/currencies/SEK/activate", performedBy: "E001");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<CurrencyResponse>(Ct)).ShouldBe(new("SEK", 2, true));
        await Eventually.AssertAsync(() =>
        {
            var activated = app.Published<CurrencyActivated>(CurrencyActivated.SchemaUri).ShouldHaveSingleItem();
            activated.Code.ShouldBe("SEK");
            activated.MinorUnits.ShouldBe(2);
            activated.OccurredOn.ShouldBe(Today);
            activated.PerformedBy.ShouldBe("E001");
            return Task.CompletedTask;
        });
        app.Broker.Sent.Single(m => m.SchemaUri == CurrencyActivated.SchemaUri).Key.ShouldBe("SEK");
        (await app.Database.ScalarAsync<string>("SELECT performed_by FROM currency WHERE code = 'SEK'")).ShouldBe("E001");
    }

    [Fact]
    public async Task Activating_an_active_currency_returns_ok_and_publishes_nothing_more()
    {
        await using var app = await StartWithOpenDayAsync();
        (await app.PostAsync("/currencies/EUR/activate")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var again = await app.PostAsync("/currencies/EUR/activate");

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<CurrencyResponse>(Ct)).ShouldBe(new("EUR", 2, true));
        await app.OutboxDrainedAsync();
        app.Published<CurrencyActivated>(CurrencyActivated.SchemaUri).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Concurrent_activations_publish_once()
    {
        await using var app = await StartWithOpenDayAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => app.PostAsync("/currencies/USD/activate")));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        await app.OutboxDrainedAsync();
        app.Published<CurrencyActivated>(CurrencyActivated.SchemaUri).ShouldHaveSingleItem().Code.ShouldBe("USD");
    }

    [Fact]
    public async Task The_code_is_not_case_sensitive()
    {
        await using var app = await StartWithOpenDayAsync();

        var response = await app.PostAsync("/currencies/sek/activate");

        (await response.Content.ReadFromJsonAsync<CurrencyResponse>(Ct)).ShouldBe(new("SEK", 2, true));
    }

    [Fact]
    public async Task An_unknown_currency_returns_404_problem_details_and_publishes_nothing()
    {
        await using var app = await StartWithOpenDayAsync();

        var response = await app.PostAsync("/currencies/XYZ/activate");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        problem!.Extensions["code"]!.ToString().ShouldBe("currency_not_found");
        (await app.Database.ScalarAsync<long>(
            $"SELECT count(*) FROM outbox_message WHERE schema_uri = '{CurrencyActivated.SchemaUri}'")).ShouldBe(0);
    }

    [Fact]
    public async Task Activation_is_listed_afterwards()
    {
        await using var app = await StartWithOpenDayAsync();
        await app.PostAsync("/currencies/SEK/activate");

        var currencies = await app.GetJsonAsync<CurrencyResponse[]>("/currencies");

        currencies.Single(c => c.Code == "SEK").IsActive.ShouldBeTrue();
        currencies.Where(c => c.Code != "SEK").ShouldAllBe(c => !c.IsActive);
    }

    // The service opens Operations:StartDate (2026-01-01 in the tests) when it starts.
    private Task<OperationsApp> StartWithOpenDayAsync() => OperationsApp.StartAsync(postgres);
}
