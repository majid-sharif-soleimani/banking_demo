using System.Net;
using Bank.Accounts.Api.Features.Accounts;
using Bank.Accounts.IntegrationTests.Support;
using Bank.Contracts;
using Bank.Testing;

namespace Bank.Accounts.IntegrationTests;

public class StatementTests(PostgresFixture postgres)
{
    private static readonly DateOnly Day1 = AccountsApp.Today;
    private static readonly DateOnly Day2 = Day1.AddDays(1);
    private static readonly DateOnly Day3 = Day1.AddDays(2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Day 1: +1,000; day 2: −200 and +50; day 3: nothing.</summary>
    private static async Task<(AccountsApp App, AccountResponse Account)> HistoryAsync(PostgresFixture postgres)
    {
        var app = await AccountsApp.StartAsync(postgres);
        var account = await app.OpenAccountAsync();
        await app.DepositAsync(account.Id, 1_000m);
        await app.OpenBusinessDayAsync(Day2);
        await app.WithdrawAsync(account.Id, 200m);
        await app.DepositAsync(account.Id, 50m);
        await app.OpenBusinessDayAsync(Day3);
        return (app, account);
    }

    [Fact]
    public async Task Statement_lists_the_period_with_opening_and_closing_balances()
    {
        var (app, account) = await HistoryAsync(postgres);
        await using var _ = app;

        var statement = await app.GetJsonAsync<StatementResponse>($"/accounts/{account.Id}/statement?from={Day2:yyyy-MM-dd}&to={Day3:yyyy-MM-dd}");

        statement.OpeningBalance.ShouldBe(new Money(1_000m, "SEK"));
        statement.Lines.Select(l => (l.Kind, l.Amount.Amount, l.BalanceAfter.Amount)).ShouldBe([("Withdrawal", -200m, 800m), ("Deposit", 50m, 850m)]);
        statement.ClosingBalance.ShouldBe(new Money(850m, "SEK"));
        statement.Number.ShouldBe(account.Number);
    }

    [Fact]
    public async Task A_period_without_entries_has_equal_opening_and_closing_balances()
    {
        var (app, account) = await HistoryAsync(postgres);
        await using var _ = app;

        var statement = await app.GetJsonAsync<StatementResponse>($"/accounts/{account.Id}/statement?from={Day3:yyyy-MM-dd}&to={Day3:yyyy-MM-dd}");

        statement.Lines.ShouldBeEmpty();
        statement.OpeningBalance.ShouldBe(statement.ClosingBalance);
        statement.ClosingBalance.Amount.ShouldBe(850m);
    }

    [Theory]
    [InlineData(-1, 0)]      // before the account had money
    [InlineData(0, 1_000)]   // end of day 1
    [InlineData(1, 850)]     // end of day 2, after both entries
    [InlineData(2, 850)]     // day 3, no entries: unchanged
    public async Task Balance_at_a_date_is_the_balance_at_the_end_of_that_business_date(int dayOffset, decimal expected)
    {
        var (app, account) = await HistoryAsync(postgres);
        await using var _ = app;

        var balance = await app.GetJsonAsync<BalanceResponse>($"/accounts/{account.Id}/balance?date={Day1.AddDays(dayOffset):yyyy-MM-dd}");

        balance.Balance.ShouldBe(new Money(expected, "SEK"));
    }

    [Fact]
    public async Task The_statement_closing_balance_matches_the_account_balance()
    {
        var (app, account) = await HistoryAsync(postgres);
        await using var _ = app;

        var statement = await app.GetJsonAsync<StatementResponse>($"/accounts/{account.Id}/statement?from={Day1:yyyy-MM-dd}&to={Day3:yyyy-MM-dd}");

        statement.ClosingBalance.Amount.ShouldBe(await app.BalanceAsync(account.Id));
        (statement.OpeningBalance.Amount + statement.Lines.Sum(l => l.Amount.Amount)).ShouldBe(statement.ClosingBalance.Amount);
    }

    [Fact]
    public async Task Invalid_periods_and_future_dates_are_rejected()
    {
        var (app, account) = await HistoryAsync(postgres);
        await using var _ = app;
        var client = app.CreateClient();

        (await client.GetAsync($"/accounts/{account.Id}/statement?from={Day3:yyyy-MM-dd}&to={Day1:yyyy-MM-dd}", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAsync($"/accounts/{account.Id}/balance?date={Day3.AddDays(1):yyyy-MM-dd}", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await app.Client(customerId: Guid.NewGuid()).GetAsync($"/accounts/{account.Id}/balance?date={Day1:yyyy-MM-dd}", Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
