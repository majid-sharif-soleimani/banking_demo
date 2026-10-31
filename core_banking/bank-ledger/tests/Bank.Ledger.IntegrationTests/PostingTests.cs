using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Ledger.Api.Features;
using Bank.Ledger.IntegrationTests.Support;
using Bank.Messaging.Inbox;
using Bank.Testing;

namespace Bank.Ledger.IntegrationTests;

public class PostingTests(PostgresFixture postgres)
{
    private static readonly DateOnly Today = LedgerApp.Today;

    private static Money Sek(decimal v) => new(v, "SEK");

    private static IIntegrationEvent[] OneOfEach(Guid account, Guid loan) =>
    [
        new MoneyDeposited(Guid.NewGuid(), Today, "E001", account, Guid.NewGuid(), Sek(10_000m), Sek(10_000m)),
        new MoneyWithdrawn(Guid.NewGuid(), Today, "E001", account, Guid.NewGuid(), Sek(1_000m), Sek(9_000m)),
        new MoneyTransferred(Guid.NewGuid(), Today, "E001", Guid.NewGuid(), account, Guid.NewGuid(), Sek(1_150m), new Money(100m, "EUR"), "0.0869565217", "fixed", null, null),
        new LoanDisbursed(Guid.NewGuid(), Today, "E001", loan, Guid.NewGuid(), Guid.NewGuid(), account, Sek(200_000m)),
        new LoanPaymentAllocated(Guid.NewGuid(), Today, "system", loan, Guid.NewGuid(), "Account", Sek(5_012.52m), Sek(1_158.33m), Sek(3.09m)),
        new LoanPaymentAllocated(Guid.NewGuid(), Today, "customer", loan, Guid.NewGuid(), "Card", Sek(1_000m), Sek(0m), Sek(0m)),
        new LoanPaymentAllocated(Guid.NewGuid(), Today, "system", loan, Guid.NewGuid(), "Payoff", Sek(193_987.48m), Sek(2_527.01m), Sek(0m)),
        // Same-currency transfer: no entry.
        new MoneyTransferred(Guid.NewGuid(), Today, "E001", Guid.NewGuid(), account, Guid.NewGuid(), Sek(50m), Sek(50m), null, null, null, null),
    ];

    private static Task DeliverAsync(LedgerApp app, IIntegrationEvent e) => e switch
    {
        MoneyDeposited d => app.DeliverAsync(d, "k"),
        MoneyWithdrawn w => app.DeliverAsync(w, "k"),
        MoneyTransferred t => app.DeliverAsync(t, "k"),
        LoanDisbursed l => app.DeliverAsync(l, "k"),
        LoanPaymentAllocated p => app.DeliverAsync(p, "k"),
        _ => throw new NotSupportedException(),
    };

    [Fact]
    public async Task Each_event_type_posts_its_entries_once()
    {
        await using var app = await LedgerApp.StartAsync(postgres);
        foreach (var e in OneOfEach(Guid.NewGuid(), Guid.NewGuid()))
        {
            await DeliverAsync(app, e);
        }

        var entries = await app.GetJsonAsync<JournalEntryResponse[]>($"/journal-entries?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}");

        // 1 + 1 + 2 (two currencies) + 1 + 3 payments = 8; the same-currency transfer posts nothing.
        entries.Length.ShouldBe(8);
        entries.ShouldAllBe(e => e.Lines.Sum(l => l.Debit) == e.Lines.Sum(l => l.Credit));
        entries.Select(e => e.SourceEvent).Distinct().ShouldBe(["MoneyDeposited", "MoneyWithdrawn", "MoneyTransferred", "LoanDisbursed", "LoanPaymentAllocated"], ignoreOrder: true);
        var card = entries.Single(e => e.Description == "Loan payment (Card)");
        card.Lines[0].Account.ShouldBe("CardClearing");
    }

    [Fact]
    public async Task A_duplicate_event_posts_nothing()
    {
        await using var app = await LedgerApp.StartAsync(postgres);
        var deposit = new MoneyDeposited(Guid.NewGuid(), Today, "E001", Guid.NewGuid(), Guid.NewGuid(), Sek(100m), Sek(100m));

        (await app.DeliverAsync(deposit, "k")).ShouldBe(ProcessOutcome.Handled);
        (await app.DeliverAsync(deposit, "k")).ShouldBe(ProcessOutcome.Duplicate);

        (await app.ScalarAsync<long>("SELECT count(*) FROM journal_entry")).ShouldBe(1);
        (await app.ScalarAsync<long>("SELECT count(*) FROM journal_line")).ShouldBe(2);
    }

    [Fact]
    public async Task The_trial_balance_is_zero_per_currency_after_a_mixed_scenario()
    {
        await using var app = await LedgerApp.StartAsync(postgres);
        foreach (var e in OneOfEach(Guid.NewGuid(), Guid.NewGuid()))
        {
            await DeliverAsync(app, e);
        }

        foreach (var currency in new[] { "SEK", "EUR" })
        {
            var balance = await app.GetJsonAsync<TrialBalanceResponse>($"/trial-balance?date={Today:yyyy-MM-dd}&currency={currency}");
            balance.TotalDebit.ShouldBe(balance.TotalCredit, currency);
        }

        var sek = await app.GetJsonAsync<TrialBalanceResponse>($"/trial-balance?currency=SEK");
        // Loans: 200,000 − 5,012.52 (account) − 1,000 (card) − 193,987.48 (payoff) = 0; interest 1,158.33 + 2,527.01 = 3,685.34; penalty 3.09; cash 10,000 − 1,000 = 9,000.
        Balance(sek, "LoansReceivable").ShouldBe(0m);
        Balance(sek, "InterestIncome").ShouldBe(3_685.34m);
        Balance(sek, "PenaltyIncome").ShouldBe(3.09m);
        Balance(sek, "Cash").ShouldBe(9_000m);
        Balance(sek, "CardClearing").ShouldBe(1_000m);
    }

    [Fact]
    public async Task The_trial_balance_at_an_earlier_date_leaves_out_later_entries()
    {
        await using var app = await LedgerApp.StartAsync(postgres);
        await app.DeliverAsync(new MoneyDeposited(Guid.NewGuid(), Today, "E001", Guid.NewGuid(), Guid.NewGuid(), Sek(100m), Sek(100m)), "k");
        await app.DeliverAsync(new MoneyDeposited(Guid.NewGuid(), Today.AddDays(1), "E001", Guid.NewGuid(), Guid.NewGuid(), Sek(50m), Sek(150m)), "k");

        Balance(await app.GetJsonAsync<TrialBalanceResponse>($"/trial-balance?date={Today:yyyy-MM-dd}&currency=SEK"), "Cash").ShouldBe(100m);
        Balance(await app.GetJsonAsync<TrialBalanceResponse>($"/trial-balance?date={Today.AddDays(1):yyyy-MM-dd}&currency=SEK"), "Cash").ShouldBe(150m);
    }

    [Fact]
    public async Task A_posting_in_a_currency_without_a_chart_fails_and_posts_nothing()
    {
        await using var app = await LedgerApp.StartAsync(postgres);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            app.DeliverAsync(new MoneyDeposited(Guid.NewGuid(), Today, "E001", Guid.NewGuid(), Guid.NewGuid(), new Money(1m, "USD"), new Money(1m, "USD")), "k"));

        (await app.ScalarAsync<long>("SELECT count(*) FROM journal_entry")).ShouldBe(0);
    }

    private static decimal Balance(TrialBalanceResponse balance, string account) => balance.Rows.Single(r => r.Name == account).Balance;
}
