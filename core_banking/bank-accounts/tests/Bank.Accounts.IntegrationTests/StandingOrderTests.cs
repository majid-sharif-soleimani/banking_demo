using System.Net;
using System.Net.Http.Json;
using Bank.Accounts.Api.Features.StandingOrders;
using Bank.Accounts.IntegrationTests.Support;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Messaging.Inbox;
using Bank.Testing;

namespace Bank.Accounts.IntegrationTests;

public class StandingOrderTests(PostgresFixture postgres)
{
    private static readonly DateOnly Today = AccountsApp.Today;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<StandingOrderResponse> CreateAsync(
        AccountsApp app, Guid from, Guid to, decimal amount, string frequency = "Monthly", DateOnly? start = null, DateOnly? end = null)
    {
        var response = await app.PostJsonAsync("/standing-orders",
            new StandingOrderEndpoints.Request(from, to, new Money(amount, "SEK"), frequency, start ?? Today, end), Guid.NewGuid().ToString());
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<StandingOrderResponse>(Ct))!;
    }

    private static Task<ProcessOutcome> CloseDayAsync(AccountsApp app, DateOnly date, Guid? eventId = null) =>
        app.DeliverAsync(new DayClosed(eventId ?? Guid.NewGuid(), date, "system", date), $"{date:yyyy-MM-dd}");

    [Fact]
    public async Task An_order_can_be_created_without_enough_money()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var from = await app.OpenAccountAsync();
        var to = await app.OpenAccountAsync();

        var order = await CreateAsync(app, from.Id, to.Id, 500m);

        order.Status.ShouldBe("Active");
        order.NextRunDate.ShouldBe(Today);
    }

    [Fact]
    public async Task End_of_day_runs_the_due_order_advances_it_and_completes_the_day()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var from = await app.OpenAccountAsync();
        var to = await app.OpenAccountAsync();
        await app.DepositAsync(from.Id, 1_000m);
        var order = await CreateAsync(app, from.Id, to.Id, 300m);

        await CloseDayAsync(app, Today);

        (await app.BalanceAsync(from.Id)).ShouldBe(700m);
        (await app.BalanceAsync(to.Id)).ShouldBe(300m);
        var after = await app.GetJsonAsync<StandingOrderResponse>($"/standing-orders/{order.Id}");
        after.NextRunDate.ShouldBe(Today.AddMonths(1));
        (await app.ScalarAsync<string>("SELECT status FROM standing_order_run")).ShouldBe("Succeeded");
        await Eventually.AssertAsync(() =>
        {
            app.Published<MoneyTransferred>().ShouldHaveSingleItem().StandingOrderId.ShouldBe(order.Id);
            var completed = app.Published<DayCompleted>().ShouldHaveSingleItem();
            completed.Date.ShouldBe(Today);
            completed.Service.ShouldBe("accounts");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Insufficient_funds_records_a_failed_run_publishes_StandingOrderFailed_and_does_not_retry()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var from = await app.OpenAccountAsync();
        var to = await app.OpenAccountAsync();
        await app.DepositAsync(from.Id, 100m);
        var order = await CreateAsync(app, from.Id, to.Id, 300m, "Weekly");

        await CloseDayAsync(app, Today);

        (await app.BalanceAsync(from.Id)).ShouldBe(100m);
        (await app.ScalarAsync<string>("SELECT status || ':' || reason FROM standing_order_run")).ShouldBe("Failed:insufficient_funds");
        (await app.GetJsonAsync<StandingOrderResponse>($"/standing-orders/{order.Id}")).NextRunDate.ShouldBe(Today.AddDays(7));
        await Eventually.AssertAsync(() =>
        {
            var failed = app.Published<StandingOrderFailed>().ShouldHaveSingleItem();
            failed.ShouldSatisfyAllConditions(
                e => e.StandingOrderId.ShouldBe(order.Id),
                e => e.CustomerId.ShouldBe(app.CustomerId),
                e => e.Amount.ShouldBe(new Money(300m, "SEK")),
                e => e.RunDate.ShouldBe(Today),
                e => e.Reason.ShouldBe("insufficient_funds"));
            app.Published<DayCompleted>().ShouldHaveSingleItem();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task The_same_DayClosed_twice_runs_and_completes_once()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var from = await app.OpenAccountAsync();
        var to = await app.OpenAccountAsync();
        await app.DepositAsync(from.Id, 1_000m);
        await CreateAsync(app, from.Id, to.Id, 300m);
        var eventId = Guid.NewGuid();

        (await CloseDayAsync(app, Today, eventId)).ShouldBe(ProcessOutcome.Handled);
        (await CloseDayAsync(app, Today, eventId)).ShouldBe(ProcessOutcome.Duplicate); // same message: inbox
        await CloseDayAsync(app, Today);                                                // same date, new message id

        (await app.BalanceAsync(to.Id)).ShouldBe(300m);
        await app.OutboxDrainedAsync();
        app.Published<DayCompleted>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_day_without_due_orders_still_completes()
    {
        await using var app = await AccountsApp.StartAsync(postgres);

        await CloseDayAsync(app, Today);

        await Eventually.AssertAsync(() =>
        {
            app.Published<DayCompleted>().ShouldHaveSingleItem().Service.ShouldBe("accounts");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Orders_not_due_yet_and_cancelled_orders_do_not_run()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var from = await app.OpenAccountAsync();
        var to = await app.OpenAccountAsync();
        await app.DepositAsync(from.Id, 1_000m);
        await CreateAsync(app, from.Id, to.Id, 100m, start: Today.AddDays(1));
        var cancelled = await CreateAsync(app, from.Id, to.Id, 100m);
        (await app.CreateClient().DeleteAsync($"/standing-orders/{cancelled.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await CloseDayAsync(app, Today);

        (await app.BalanceAsync(to.Id)).ShouldBe(0m);
        (await app.GetJsonAsync<StandingOrderResponse>($"/standing-orders/{cancelled.Id}")).ShouldSatisfyAllConditions(
            o => o.Status.ShouldBe("Cancelled"), o => o.NextRunDate.ShouldBeNull());
    }

    [Fact]
    public async Task An_order_whose_end_date_has_passed_completes()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var from = await app.OpenAccountAsync();
        var to = await app.OpenAccountAsync();
        await app.DepositAsync(from.Id, 1_000m);
        var order = await CreateAsync(app, from.Id, to.Id, 100m, end: Today);

        await CloseDayAsync(app, Today);

        (await app.GetJsonAsync<StandingOrderResponse>($"/standing-orders/{order.Id}")).ShouldSatisfyAllConditions(
            o => o.Status.ShouldBe("Completed"), o => o.NextRunDate.ShouldBeNull());
    }

    [Fact]
    public async Task Several_orders_on_one_day_each_succeed_or_fail_on_their_own()
    {
        // 250 available: the first order (200) succeeds, the second (200) fails, the third (50) succeeds. Each has its own transaction.
        await using var app = await AccountsApp.StartAsync(postgres);
        var from = await app.OpenAccountAsync();
        var to = await app.OpenAccountAsync();
        await app.DepositAsync(from.Id, 250m);
        await CreateAsync(app, from.Id, to.Id, 200m);
        await CreateAsync(app, from.Id, to.Id, 200m);
        await CreateAsync(app, from.Id, to.Id, 50m);

        await CloseDayAsync(app, Today);

        (await app.BalanceAsync(to.Id)).ShouldBe(250m);
        (await app.ScalarAsync<long>("SELECT count(*) FROM standing_order_run WHERE status = 'Failed'")).ShouldBe(1);
    }

    [Theory]
    [InlineData("Daily", 0, "invalid_frequency")]
    [InlineData("Monthly", -1, "invalid_dates")]
    public async Task Invalid_orders_are_rejected(string frequency, int startOffset, string code)
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var from = await app.OpenAccountAsync();
        var to = await app.OpenAccountAsync();

        var response = await app.PostJsonAsync("/standing-orders",
            new StandingOrderEndpoints.Request(from.Id, to.Id, new Money(10m, "SEK"), frequency, Today.AddDays(startOffset), null), "key");

        (await AccountTests.Code(response)).ShouldBe(code);
    }

    [Fact]
    public async Task Another_customers_order_is_hidden()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var from = await app.OpenAccountAsync();
        var to = await app.OpenAccountAsync();
        var order = await CreateAsync(app, from.Id, to.Id, 10m);

        (await app.Client(customerId: Guid.NewGuid()).GetAsync($"/standing-orders/{order.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await app.Client(customerId: Guid.NewGuid()).DeleteAsync($"/standing-orders/{order.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_retried_create_with_the_same_key_makes_one_order()
    {
        await using var app = await AccountsApp.StartAsync(postgres);
        var from = await app.OpenAccountAsync();
        var to = await app.OpenAccountAsync();
        var request = new StandingOrderEndpoints.Request(from.Id, to.Id, new Money(10m, "SEK"), "Weekly", Today, null);

        await app.PostJsonAsync("/standing-orders", request, "so-1");
        (await app.PostJsonAsync("/standing-orders", request, "so-1")).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await app.ScalarAsync<long>("SELECT count(*) FROM standing_order")).ShouldBe(1);
    }
}
