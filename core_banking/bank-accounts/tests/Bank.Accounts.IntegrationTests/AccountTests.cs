using System.Net;
using System.Net.Http.Json;
using Bank.Accounts.Api.Features.Accounts;
using Bank.Accounts.Api.Logic;
using Bank.Accounts.IntegrationTests.Support;
using Bank.Common;
using Bank.Contracts.Events;
using Bank.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bank.Accounts.IntegrationTests;

public class AccountTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Open_creates_an_empty_account_with_a_valid_number_and_publishes_AccountOpened()
    {
        await using var app = await AccountsApp.StartAsync(postgres);

        var account = await app.OpenAccountAsync("EUR");

        account.Number.Length.ShouldBe(9);
        Luhn.IsValid(account.Number).ShouldBeTrue();
        account.ShouldSatisfyAllConditions(
            a => a.Balance.ShouldBe(new Bank.Contracts.Money(0m, "EUR")),
            a => a.Type.ShouldBe("Current"),
            a => a.OpenedOn.ShouldBe(AccountsApp.Today));
        await Eventually.AssertAsync(() =>
        {
            var opened = app.Published<AccountOpened>().ShouldHaveSingleItem();
            opened.AccountNumber.ShouldBe(account.Number);
            opened.Currency.ShouldBe("EUR");
            opened.CustomerId.ShouldBe(app.CustomerId);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task A_customer_not_known_yet_is_rejected_with_422()
    {
        await using var app = await AccountsApp.StartAsync(postgres);

        var response = await app.PostJsonAsync("/accounts", new OpenAccount.Request(Guid.NewGuid(), "Current", "SEK"));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await Code(response)).ShouldBe("customer_not_found");
    }

    [Fact]
    public async Task The_same_request_succeeds_once_CustomerRegistered_has_arrived()
    {
        // ADR 0001: reject now, the caller retries; eventual consistency closes the gap.
        await using var app = await AccountsApp.StartAsync(postgres);
        var customer = Guid.NewGuid();
        (await app.PostJsonAsync("/accounts", new OpenAccount.Request(customer, "Savings", "SEK"))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        await app.RegisterCustomerAsync(customer);

        (await app.PostJsonAsync("/accounts", new OpenAccount.Request(customer, "Savings", "SEK"))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task An_inactive_currency_is_rejected_with_422()
    {
        await using var app = await AccountsApp.StartAsync(postgres);

        var response = await app.PostJsonAsync("/accounts", new OpenAccount.Request(app.CustomerId, "Current", "GBP"));

        (await Code(response)).ShouldBe("currency_not_active");
    }

    [Fact]
    public async Task An_unknown_account_type_is_rejected_with_400()
    {
        await using var app = await AccountsApp.StartAsync(postgres);

        var response = await app.PostJsonAsync("/accounts", new OpenAccount.Request(app.CustomerId, "Checking", "SEK"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Code(response)).ShouldBe("invalid_account_type");
    }

    [Fact]
    public async Task A_taken_number_is_drawn_again()
    {
        await using var app = await AccountsApp.StartAsync(postgres, services =>
        {
            services.RemoveAll<IAccountNumberGenerator>();
            services.AddSingleton<IAccountNumberGenerator>(new ScriptedAccountNumbers("123456782", "123456782", "100000017"));
        });

        (await app.OpenAccountAsync()).Number.ShouldBe("123456782");
        (await app.OpenAccountAsync()).Number.ShouldBe("100000017");
        await app.OutboxDrainedAsync();
        app.Published<AccountOpened>().Count.ShouldBe(2);
    }

    [Fact]
    public async Task Accounts_are_listed_per_customer_and_hidden_from_other_customers()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var mine = await app.OpenAccountAsync();
        var other = Guid.NewGuid();
        await app.RegisterCustomerAsync(other);
        await app.OpenAccountAsync(customerId: other);

        (await app.GetJsonAsync<AccountResponse[]>($"/accounts?customerId={app.CustomerId}")).ShouldHaveSingleItem().Id.ShouldBe(mine.Id);
        (await app.Client(customerId: other).GetAsync($"/accounts/{mine.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await app.Client(customerId: app.CustomerId).GetAsync($"/accounts/{mine.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await app.CreateClient().GetAsync($"/accounts/{Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reference_data_events_are_idempotent()
    {
        await using var app = await AccountsApp.StartAsync(postgres);

        await app.RegisterCustomerAsync(app.CustomerId);
        await app.DeliverAsync(new CurrencyActivated(Guid.NewGuid(), AccountsApp.Today, "system", "SEK", 2), "SEK");

        (await app.ScalarAsync<long>("SELECT count(*) FROM customer_ref")).ShouldBe(1);
        (await app.ScalarAsync<long>("SELECT count(*) FROM currency_ref")).ShouldBe(3);
    }

    internal static async Task<string> Code(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Extensions["code"]!.ToString()!;
}
