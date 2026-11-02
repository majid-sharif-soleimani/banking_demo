using Bank.EndToEndTests.Support;

namespace Bank.EndToEndTests.Scenarios;

/// <summary>
/// Scenario 1: a customer registers, opens SEK and EUR accounts, deposits, and transfers SEK to EUR. The statements, the
/// account balances and the ledger must tell the same story in both currencies.
/// </summary>
public sealed class S1CrossCurrencyTransferTests(BankFixture bank) : IClassFixture<BankFixture>
{
    [Fact]
    public async Task A_cross_currency_transfer_agrees_in_statements_and_ledger()
    {
        var anna = await bank.RegisterCustomerAsync("Anna Lind", "anna.lind@example.com");
        var sek = await bank.OpenAccountAsync(anna, "SEK");
        var eur = await bank.OpenAccountAsync(anna, "EUR");
        await bank.DepositAsync(sek, 10_000m);

        // 1,150 SEK at the demo rate of 11.50 SEK per EUR (Accounts' Fixed provider) is 100 EUR.
        var transfer = await bank.Customer(anna.Id).PostWhenKnownAsync<TransferView>("/api/accounts/transfers",
            new { fromAccountId = sek.Id, toAccountId = eur.Id, amount = new Money(1_150m, "SEK"), description = "To my euro account" }, idempotent: true);
        transfer.ConvertedAmount.ShouldBe(new Money(100.00m, "EUR"));

        var today = await bank.TodayAsync();
        await bank.CloseDayAsync();

        (await bank.AccountAsync(sek)).Balance.ShouldBe(new Money(8_850.00m, "SEK"));
        (await bank.AccountAsync(eur)).Balance.ShouldBe(new Money(100.00m, "EUR"));

        var sekStatement = await bank.Customer(anna.Id).GetAsync<StatementView>($"/api/accounts/accounts/{sek.Id}/statement?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        sekStatement.Lines.Select(l => l.Amount.Amount).ShouldBe([10_000.00m, -1_150.00m]);
        sekStatement.ClosingBalance.ShouldBe(new Money(8_850.00m, "SEK"));
        var eurStatement = await bank.Customer(anna.Id).GetAsync<StatementView>($"/api/accounts/accounts/{eur.Id}/statement?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        eurStatement.Lines.Single().Amount.ShouldBe(new Money(100.00m, "EUR"));

        // The ledger: the deposit and the transfer, the transfer as one entry per currency, each balanced through the FX position.
        var entries = await Eventually.Value(
            () => bank.Staff.GetAsync<List<JournalEntryView>>($"/api/ledger/journal-entries?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}"),
            e => e.Count == 3, "three journal entries");
        entries.Where(e => e.SourceEvent == "MoneyTransferred").Select(e => e.Currency).ShouldBe(["EUR", "SEK"], ignoreOrder: true);
        entries.ShouldAllBe(e => e.Lines.Sum(l => l.Debit) == e.Lines.Sum(l => l.Credit));

        // Customer deposits in the ledger equal the customers' balances, per currency.
        foreach (var (currency, expected) in new[] { ("SEK", 8_850.00m), ("EUR", 100.00m) })
        {
            var balance = await bank.TrialBalanceAsync(currency);
            balance.TotalDebit.ShouldBe(balance.TotalCredit);
            balance.BalanceOf(Gl.CustomerDeposits).ShouldBe(expected, $"customer deposits in {currency}");
        }
    }
}
