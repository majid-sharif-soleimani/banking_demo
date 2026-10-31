using System.Net;
using System.Net.Http.Json;
using Bank.Accounts.Api.Features.Movements;
using Bank.Accounts.IntegrationTests.Support;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Testing;

namespace Bank.Accounts.IntegrationTests;

public class CashMovementTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deposit_credits_the_account_records_an_entry_and_publishes_MoneyDeposited()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var account = await app.OpenAccountAsync();

        var response = await app.DepositAsync(account.Id, 1_000.50m);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var entry = (await response.Content.ReadFromJsonAsync<EntryResponse>(Ct))!;
        entry.ShouldSatisfyAllConditions(
            e => e.Amount.ShouldBe(new Money(1_000.50m, "SEK")),
            e => e.BalanceAfter.ShouldBe(new Money(1_000.50m, "SEK")),
            e => e.Kind.ShouldBe("Deposit"),
            e => e.BusinessDate.ShouldBe(AccountsApp.Today));
        (await app.BalanceAsync(account.Id)).ShouldBe(1_000.50m);
        await Eventually.AssertAsync(() =>
        {
            var deposited = app.Published<MoneyDeposited>().ShouldHaveSingleItem();
            deposited.EntryId.ShouldBe(entry.EntryId);
            deposited.BalanceAfter.ShouldBe(new Money(1_000.50m, "SEK"));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Withdrawal_debits_the_account_and_publishes_MoneyWithdrawn()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var account = await app.OpenAccountAsync();
        await app.DepositAsync(account.Id, 500m);

        var entry = await (await app.WithdrawAsync(account.Id, 200m)).Content.ReadFromJsonAsync<EntryResponse>(Ct);

        entry!.Amount.ShouldBe(new Money(-200m, "SEK"));
        entry.BalanceAfter.ShouldBe(new Money(300m, "SEK"));
        await Eventually.AssertAsync(() =>
        {
            app.Published<MoneyWithdrawn>().ShouldHaveSingleItem().Amount.ShouldBe(new Money(200m, "SEK"));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task An_overdraft_is_rejected_with_422_and_changes_nothing()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var account = await app.OpenAccountAsync();
        await app.DepositAsync(account.Id, 100m);

        var response = await app.WithdrawAsync(account.Id, 100.01m);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await AccountTests.Code(response)).ShouldBe("insufficient_funds");
        (await app.BalanceAsync(account.Id)).ShouldBe(100m);
        (await app.ScalarAsync<long>("SELECT count(*) FROM account_entry")).ShouldBe(1);
    }

    [Fact]
    public async Task Withdrawing_the_whole_balance_is_allowed()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var account = await app.OpenAccountAsync();
        await app.DepositAsync(account.Id, 100m);

        (await app.WithdrawAsync(account.Id, 100m)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await app.BalanceAsync(account.Id)).ShouldBe(0m);
    }

    [Fact]
    public async Task A_repeated_Idempotency_Key_returns_the_same_result_and_moves_money_once()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var account = await app.OpenAccountAsync();

        var first = await (await app.DepositAsync(account.Id, 250m, key: "dep-1")).Content.ReadAsStringAsync(Ct);
        var second = await app.DepositAsync(account.Id, 250m, key: "dep-1");

        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync(Ct)).ShouldBe(first);
        (await app.BalanceAsync(account.Id)).ShouldBe(250m);
        await app.OutboxDrainedAsync();
        app.Published<MoneyDeposited>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_same_key_with_a_different_body_is_rejected()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var account = await app.OpenAccountAsync();
        await app.DepositAsync(account.Id, 250m, key: "dep-1");

        var response = await app.DepositAsync(account.Id, 300m, key: "dep-1");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await AccountTests.Code(response)).ShouldBe("idempotency_key_reused");
    }

    [Fact]
    public async Task Concurrent_requests_with_one_key_move_money_once()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var account = await app.OpenAccountAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => app.DepositAsync(account.Id, 10m, key: "same")));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        (await app.BalanceAsync(account.Id)).ShouldBe(10m);
    }

    [Fact]
    public async Task A_failed_withdrawal_is_not_stored_so_the_same_key_works_after_a_deposit()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var account = await app.OpenAccountAsync();

        (await app.WithdrawAsync(account.Id, 50m, key: "w-1")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        await app.DepositAsync(account.Id, 100m);

        (await app.WithdrawAsync(account.Id, 50m, key: "w-1")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Two_concurrent_withdrawals_cannot_overdraw()
    {
        // Balance 100, two withdrawals of 80 at the same moment: one wins; the other conflicts on xmin, runs again on the
        // new balance 20 and is refused. Repeated to give the race a real chance.
        await using var app = await AccountsApp.StartAsync(postgres);
        for (var round = 0; round < 10; round++)
        {
            var account = await app.OpenAccountAsync();
            await app.DepositAsync(account.Id, 100m);

            var responses = await Task.WhenAll(app.WithdrawAsync(account.Id, 80m), app.WithdrawAsync(account.Id, 80m));

            responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1, $"round {round}");
            responses.Count(r => r.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict).ShouldBe(1);
            (await app.BalanceAsync(account.Id)).ShouldBe(20m);
        }
    }

    [Fact]
    public async Task Many_concurrent_deposits_all_land()
    {
        // Ten deposits at once: each conflict reruns once, so at most a few 409s; whatever succeeded is in the balance.
        await using var app = await AccountsApp.StartAsync(postgres);
        var account = await app.OpenAccountAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => app.DepositAsync(account.Id, 1m)));

        var succeeded = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        responses.Where(r => r.StatusCode != HttpStatusCode.OK).ShouldAllBe(r => r.StatusCode == HttpStatusCode.Conflict);
        (await app.BalanceAsync(account.Id)).ShouldBe(succeeded);
        (await app.ScalarAsync<long>("SELECT count(*) FROM account_entry")).ShouldBe(succeeded);
    }

    [Theory]
    [InlineData(0, "SEK", "invalid_amount")]
    [InlineData(-5, "SEK", "invalid_amount")]
    [InlineData(10.001, "SEK", "invalid_amount")]
    [InlineData(10, "EUR", "currency_mismatch")]
    public async Task Invalid_amounts_are_rejected(decimal amount, string currency, string code)
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var account = await app.OpenAccountAsync();

        (await AccountTests.Code(await app.DepositAsync(account.Id, amount, currency))).ShouldBe(code);
    }

    [Fact]
    public async Task A_movement_without_an_Idempotency_Key_is_rejected()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var account = await app.OpenAccountAsync();

        var response = await app.CreateClient().PostAsJsonAsync($"/accounts/{account.Id}/deposits", new CashMovements.Request(new Money(1m, "SEK"), null), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AccountTests.Code(response)).ShouldBe("idempotency_key_required");
    }

    [Fact]
    public async Task A_movement_on_an_unknown_account_is_404()
    {
        await using var app = await AccountsApp.StartAsync(postgres);

        (await app.DepositAsync(Guid.NewGuid(), 10m)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_customer_reusing_a_key_does_not_see_the_stored_answer()
    {
        // Review of 2026-10-31: the replay must not leak the owner's entry and balance to another customer.
        await using var app = await AccountsApp.StartAsync(postgres);
        var account = await app.OpenAccountAsync();
        var body = new CashMovements.Request(new Money(100m, "SEK"), null);
        await app.Client(customerId: app.CustomerId, idempotencyKey: "k1").PostAsJsonAsync($"/accounts/{account.Id}/deposits", body, Ct);

        var response = await app.Client(customerId: Guid.NewGuid(), idempotencyKey: "k1").PostAsJsonAsync($"/accounts/{account.Id}/deposits", body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await app.BalanceAsync(account.Id)).ShouldBe(100m);
    }
}
