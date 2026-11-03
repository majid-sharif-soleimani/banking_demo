using System.Net;
using System.Net.Http.Json;
using Bank.Accounts.Api.Features;
using Bank.Accounts.Api.Features.Movements;
using Bank.Accounts.IntegrationTests.Support;
using Bank.Contracts;
using Bank.Testing;
using Microsoft.AspNetCore.Mvc;

namespace Bank.Accounts.IntegrationTests;

public class TransactionSearchTests(PostgresFixture postgres)
{
    private static readonly DateOnly Day1 = AccountsApp.Today;
    private static readonly DateOnly Day2 = AccountsApp.Today.AddDays(1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SearchTransactions.Request Search(
        DateOnly? from = null, DateOnly? to = null, Money? min = null, Money? max = null, string? currency = null,
        IReadOnlyList<string>? kinds = null, Guid? customerId = null, Guid? accountId = null, int? limit = null, string? cursor = null) =>
        new(from, to, min, max, currency, kinds, customerId, accountId, limit, cursor);

    private static Task<HttpResponseMessage> PostAsync(AccountsApp app, SearchTransactions.Request request, string? performedBy = "E001", Guid? customerId = null) =>
        app.Client(performedBy, customerId).PostAsJsonAsync("/transactions/search", request, Ct);

    private static async Task<Page<TransactionItem>> PageAsync(AccountsApp app, SearchTransactions.Request request)
    {
        var response = await PostAsync(app, request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Page<TransactionItem>>(Ct))!;
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Extensions["code"]!.ToString()!;

    /// <summary>
    /// Day 1: A +1,000 SEK, B +20,000 SEK, C +500 EUR. Day 2: B −15,000 SEK, A → B 300 SEK.
    /// A and C belong to the app's customer, B to another one. The two entries of one transfer have no fixed order.
    /// </summary>
    private static async Task<(Guid A, Guid B, Guid C, Guid Other)> SeedAsync(AccountsApp app)
    {
        var other = Guid.CreateVersion7();
        await app.RegisterCustomerAsync(other);
        var a = (await app.OpenAccountAsync()).Id;
        var b = (await app.OpenAccountAsync(customerId: other)).Id;
        var c = (await app.OpenAccountAsync("EUR")).Id;
        (await app.DepositAsync(a, 1_000m)).EnsureSuccessStatusCode();
        (await app.DepositAsync(b, 20_000m)).EnsureSuccessStatusCode();
        (await app.DepositAsync(c, 500m, "EUR")).EnsureSuccessStatusCode();
        await app.OpenBusinessDayAsync(Day2);
        (await app.WithdrawAsync(b, 15_000m)).EnsureSuccessStatusCode();
        (await app.TransferAsync(a, b, 300m)).EnsureSuccessStatusCode();
        return (a, b, c, other);
    }

    [Fact]
    public async Task Pages_list_every_entry_oldest_first_with_its_account_and_currency()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var (a, b, c, other) = await SeedAsync(app);

        var first = await PageAsync(app, Search(limit: 4));
        var last = await PageAsync(app, Search(limit: 4, cursor: first.NextCursor));

        var all = first.Items.Concat(last.Items).ToList();
        all.Take(4).Select(t => (t.AccountId, t.Kind, t.Amount)).ShouldBe(
        [
            (a, "Deposit", new Money(1_000m, "SEK")), (b, "Deposit", new Money(20_000m, "SEK")), (c, "Deposit", new Money(500m, "EUR")),
            (b, "Withdrawal", new Money(-15_000m, "SEK")),
        ]);
        all.Skip(4).Select(t => (t.AccountId, t.Kind, t.Amount)).ShouldBe(
            [(b, "TransferIn", new Money(300m, "SEK")), (a, "TransferOut", new Money(-300m, "SEK"))], ignoreOrder: true);
        all.Select(t => t.BusinessDate).ShouldBe([Day1, Day1, Day1, Day2, Day2, Day2]);
        all[3].ShouldSatisfyAllConditions(t => t.CustomerId.ShouldBe(other), t => t.BalanceAfter.ShouldBe(new Money(5_000m, "SEK")));
        first.Items.Count.ShouldBe(4);
        last.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task An_entry_posted_while_paging_comes_at_the_end_and_a_page_as_long_as_the_rest_is_the_last()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var (a, _, _, _) = await SeedAsync(app);

        var first = await PageAsync(app, Search(limit: 3));
        (await app.DepositAsync(a, 42m)).EnsureSuccessStatusCode();
        var rest = await PageAsync(app, Search(limit: 4, cursor: first.NextCursor));

        rest.Items[0].Amount.Amount.ShouldBe(-15_000m);
        rest.Items.Skip(1).Take(2).Select(t => t.Amount.Amount).ShouldBe([300m, -300m], ignoreOrder: true);
        rest.Items[^1].Amount.Amount.ShouldBe(42m);
        rest.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task Dates_amount_sizes_and_currency_filter_the_entries()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        await SeedAsync(app);

        (await PageAsync(app, Search(from: Day2))).Items.Count.ShouldBe(3);
        (await PageAsync(app, Search(from: Day1, to: Day1))).Items.Count.ShouldBe(3);
        // Size, not sign: the 20,000 deposit and the 15,000 withdrawal.
        (await PageAsync(app, Search(min: new Money(10_000m, "SEK")))).Items.Select(t => t.Amount.Amount).ShouldBe([20_000m, -15_000m]);
        (await PageAsync(app, Search(min: new Money(300m, "SEK"), max: new Money(1_000m, "SEK")))).Items.Select(t => t.Amount.Amount)
            .ShouldBe([1_000m, 300m, -300m], ignoreOrder: true);
        (await PageAsync(app, Search(currency: "eur"))).Items.ShouldHaveSingleItem().Amount.ShouldBe(new Money(500m, "EUR"));
        // An amount in EUR searches EUR only: the 500 EUR deposit, not the 1,000 SEK one.
        (await PageAsync(app, Search(min: new Money(400m, "EUR")))).Items.ShouldHaveSingleItem().Amount.Currency.ShouldBe("EUR");
    }

    [Fact]
    public async Task Movement_types_customer_and_account_filter_the_entries()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var (a, b, _, other) = await SeedAsync(app);

        (await PageAsync(app, Search(kinds: ["TransferOut", "transferin"]))).Items.Select(t => t.AccountId).ShouldBe([a, b], ignoreOrder: true);
        (await PageAsync(app, Search(customerId: other))).Items.ShouldAllBe(t => t.AccountId == b);
        (await PageAsync(app, Search(customerId: other))).Items.Count.ShouldBe(3);
        (await PageAsync(app, Search(accountId: a))).Items.Select(t => t.Kind).ShouldBe(["Deposit", "TransferOut"]);
        (await PageAsync(app, Search(customerId: app.CustomerId, kinds: ["Deposit"]))).Items.Count.ShouldBe(2);
        (await PageAsync(app, Search(accountId: Guid.NewGuid()))).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_customer_and_a_caller_without_an_actor_are_refused()
    {
        await using var app = await AccountsApp.StartAsync(postgres);

        var customer = await PostAsync(app, Search(), performedBy: null, customerId: app.CustomerId);
        var anonymous = await PostAsync(app, Search(), performedBy: null);

        customer.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(customer)).ShouldBe("staff_only");
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(anonymous)).ShouldBe("missing_actor");
    }

    [Fact]
    public async Task Bad_filters_and_paging_are_bad_requests()
    {
        await using var app = await AccountsApp.StartAsync(postgres);

        var cases = new (SearchTransactions.Request Request, string Code)[]
        {
            (Search(to: Day1.AddDays(1)), "date_in_future"),
            (Search(from: Day1, to: Day1.AddDays(-1)), "invalid_period"),
            (Search(min: new Money(10m, "SEK"), currency: "EUR"), "currency_mismatch"),
            (Search(min: new Money(10m, "SEK"), max: new Money(5m, "SEK")), "invalid_amount_range"),
            (Search(kinds: ["Fee"]), "invalid_kind"),
            (Search(limit: 201), "invalid_limit"),
            (Search(cursor: "%%%"), "invalid_cursor"),
        };

        foreach (var (request, code) in cases)
        {
            var response = await PostAsync(app, request);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, code);
            (await CodeAsync(response)).ShouldBe(code);
        }
    }
}
