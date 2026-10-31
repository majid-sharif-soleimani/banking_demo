using System.Net;
using System.Net.Http.Json;
using Bank.Accounts.Api.Features.Transfers;
using Bank.Accounts.IntegrationTests.Support;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Testing;

namespace Bank.Accounts.IntegrationTests;

public class TransferTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Same_currency_transfer_moves_the_amount_and_publishes_MoneyTransferred_without_fx()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var from = await app.OpenAccountAsync();
        var to = await app.OpenAccountAsync();
        await app.DepositAsync(from.Id, 1_000m);

        var response = await app.TransferAsync(from.Id, to.Id, 400m);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var transfer = (await response.Content.ReadFromJsonAsync<TransferResponse>(Ct))!;
        transfer.ConvertedAmount.ShouldBe(new Money(400m, "SEK"));
        transfer.FxRate.ShouldBeNull();
        (await app.BalanceAsync(from.Id)).ShouldBe(600m);
        (await app.BalanceAsync(to.Id)).ShouldBe(400m);
        await Eventually.AssertAsync(() =>
        {
            var moved = app.Published<MoneyTransferred>().ShouldHaveSingleItem();
            moved.ShouldSatisfyAllConditions(
                e => e.TransferId.ShouldBe(transfer.TransferId),
                e => e.Amount.ShouldBe(new Money(400m, "SEK")),
                e => e.FxRate.ShouldBeNull(),
                e => e.Fee.ShouldBeNull(),
                e => e.StandingOrderId.ShouldBeNull());
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Cross_currency_transfer_converts_and_stores_the_rate_source_and_time()
    {
        // 100 EUR × 11.50 SEK/EUR = 1,150.00 SEK (fixed development rate).
        await using var app = await AccountsApp.StartAsync(postgres);
        var eur = await app.OpenAccountAsync("EUR");
        var sek = await app.OpenAccountAsync("SEK");
        await app.DepositAsync(eur.Id, 100m, "EUR");

        var transfer = await (await app.TransferAsync(eur.Id, sek.Id, 100m, "EUR")).Content.ReadFromJsonAsync<TransferResponse>(Ct);

        transfer!.ConvertedAmount.ShouldBe(new Money(1_150.00m, "SEK"));
        transfer.FxRate.ShouldBe(11.50m);
        transfer.FxSource.ShouldBe("fixed");
        transfer.FxObservedOn.ShouldBe(AccountsApp.Today);
        (await app.ScalarAsync<bool>($"SELECT fx_at IS NOT NULL FROM transfer WHERE id = '{transfer.TransferId}'")).ShouldBeTrue();
        (await app.BalanceAsync(eur.Id)).ShouldBe(0m);
        (await app.BalanceAsync(sek.Id)).ShouldBe(1_150m);
        await Eventually.AssertAsync(() =>
        {
            var moved = app.Published<MoneyTransferred>().ShouldHaveSingleItem();
            moved.FxRate.ShouldBe("11.50");
            moved.FxSource.ShouldBe("fixed");
            moved.ConvertedAmount.ShouldBe(new Money(1_150m, "SEK"));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Sek_to_eur_uses_the_inverted_rate_and_rounds_to_cents()
    {
        // 100 SEK × (1 / 11.50 = 0.0869565217) = 8.69565… → 8.70 EUR
        await using var app = await AccountsApp.StartAsync(postgres);
        var sek = await app.OpenAccountAsync("SEK");
        var eur = await app.OpenAccountAsync("EUR");
        await app.DepositAsync(sek.Id, 100m);

        var transfer = await (await app.TransferAsync(sek.Id, eur.Id, 100m)).Content.ReadFromJsonAsync<TransferResponse>(Ct);

        transfer!.ConvertedAmount.ShouldBe(new Money(8.70m, "EUR"));
        transfer.FxRate.ShouldBe(0.0869565217m);
    }

    [Fact]
    public async Task Eur_to_usd_is_crossed_through_sek()
    {
        // 10 EUR × (11.50 / 10.60 = 1.0849056604) = 10.849… → 10.85 USD
        await using var app = await AccountsApp.StartAsync(postgres);
        var eur = await app.OpenAccountAsync("EUR");
        var usd = await app.OpenAccountAsync("USD");
        await app.DepositAsync(eur.Id, 10m, "EUR");

        var transfer = await (await app.TransferAsync(eur.Id, usd.Id, 10m, "EUR")).Content.ReadFromJsonAsync<TransferResponse>(Ct);

        transfer!.ConvertedAmount.ShouldBe(new Money(10.85m, "USD"));
    }

    [Fact]
    public async Task No_rate_for_the_pair_gives_fx_unavailable_and_moves_nothing()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        await app.DeliverAsync(new CurrencyActivated(Guid.NewGuid(), AccountsApp.Today, "system", "GBP", 2), "GBP");
        var sek = await app.OpenAccountAsync("SEK");
        var gbp = await app.OpenAccountAsync("GBP");
        await app.DepositAsync(sek.Id, 100m);

        var response = await app.TransferAsync(sek.Id, gbp.Id, 50m);

        (await AccountTests.Code(response)).ShouldBe("fx_unavailable");
        (await app.BalanceAsync(sek.Id)).ShouldBe(100m);
    }

    [Theory]
    [InlineData(150, "insufficient_funds")]
    [InlineData(0.01, "amount_too_small")]
    public async Task Rule_violations_give_422(decimal amount, string code)
    {
        // 0.01 SEK → EUR = 0.00087 → 0.00: nothing would arrive.
        await using var app = await AccountsApp.StartAsync(postgres);
        var sek = await app.OpenAccountAsync("SEK");
        var eur = await app.OpenAccountAsync("EUR");
        await app.DepositAsync(sek.Id, 100m);

        (await AccountTests.Code(await app.TransferAsync(sek.Id, eur.Id, amount))).ShouldBe(code);
    }

    [Fact]
    public async Task A_transfer_to_the_same_account_or_an_unknown_account_is_rejected()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var account = await app.OpenAccountAsync();
        await app.DepositAsync(account.Id, 100m);

        (await AccountTests.Code(await app.TransferAsync(account.Id, account.Id, 10m))).ShouldBe("same_account");
        (await AccountTests.Code(await app.TransferAsync(account.Id, Guid.NewGuid(), 10m))).ShouldBe("target_account_not_found");
    }

    [Fact]
    public async Task A_transfer_from_another_customers_account_is_404()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var from = await app.OpenAccountAsync();
        var to = await app.OpenAccountAsync();
        await app.DepositAsync(from.Id, 100m);

        var response = await app.Client(customerId: Guid.NewGuid(), idempotencyKey: "k")
            .PostAsJsonAsync("/transfers", new TransferEndpoints.Request(from.Id, to.Id, new Money(10m, "SEK"), null), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_repeated_key_transfers_once()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var from = await app.OpenAccountAsync();
        var to = await app.OpenAccountAsync();
        await app.DepositAsync(from.Id, 100m);

        await app.TransferAsync(from.Id, to.Id, 30m, key: "t-1");
        await app.TransferAsync(from.Id, to.Id, 30m, key: "t-1");

        (await app.BalanceAsync(to.Id)).ShouldBe(30m);
    }

    [Fact]
    public async Task Opposite_concurrent_transfers_never_create_or_lose_money()
    {
        // A → B and B → A at the same moment lock the two rows in opposite order; a deadlock or version conflict reruns once.
        await using var app = await AccountsApp.StartAsync(postgres);
        var a = await app.OpenAccountAsync();
        var b = await app.OpenAccountAsync();
        await app.DepositAsync(a.Id, 1_000m);
        await app.DepositAsync(b.Id, 1_000m);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).SelectMany(_ => new[]
        {
            app.TransferAsync(a.Id, b.Id, 10m), app.TransferAsync(b.Id, a.Id, 10m),
        }));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict);
        ((await app.BalanceAsync(a.Id)) + (await app.BalanceAsync(b.Id))).ShouldBe(2_000m);
        (await app.ScalarAsync<decimal>("SELECT sum(amount) FROM account_entry")).ShouldBe(2_000m);
    }
}
