using Bank.EndToEndTests.Support;

namespace Bank.EndToEndTests.Scenarios;

/// <summary>
/// Scenario 5: a weekly standing order runs at end of day. The first run fails for lack of funds and the customer is
/// emailed; after a deposit, the next run a week later pays.
/// </summary>
public sealed class S5StandingOrderTests(BankFixture bank) : IClassFixture<BankFixture>
{
    [Fact]
    public async Task A_failed_standing_order_is_emailed_and_the_next_run_pays()
    {
        var eva = await bank.RegisterCustomerAsync("Eva Strand", "eva.strand@example.com");
        var from = await bank.OpenAccountAsync(eva, "SEK");
        var savings = await bank.OpenAccountAsync(eva, "SEK", "Savings");
        var start = await bank.TodayAsync();
        await bank.Customer(eva.Id).PostWhenKnownAsync<StandingOrderView>("/api/accounts/standing-orders", new
        {
            fromAccountId = from.Id, toAccountId = savings.Id, amount = new Money(500m, "SEK"), frequency = "Weekly", startDate = start, endDate = (DateOnly?)null,
        }, idempotent: true);

        // The first run, at end of the start date, fails: the account is empty.
        await bank.CloseDayAsync();
        (await Eventually.Value(() => bank.EmailSubjectsAsync(eva.Email), s => s.Count >= 1, "the standing order email"))
            .ShouldContain("Your standing order could not be paid");
        (await bank.AccountAsync(savings)).Balance.Amount.ShouldBe(0m);

        // After a deposit, the next run a week later pays.
        await bank.DepositAsync(from, 1_000m);
        await bank.CloseDaysUntilAsync(start.AddDays(8));
        (await bank.AccountAsync(savings)).Balance.ShouldBe(new Money(500.00m, "SEK"));
        (await bank.AccountAsync(from)).Balance.ShouldBe(new Money(500.00m, "SEK"));
        (await bank.EmailSubjectsAsync(eva.Email)).Count(s => s == "Your standing order could not be paid").ShouldBe(1);
    }
}
