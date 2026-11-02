using System.Net;
using Bank.Contracts;
using Bank.Lending.Api.Features.Loans;
using Bank.Lending.IntegrationTests.Support;
using Bank.Testing;

namespace Bank.Lending.IntegrationTests;

public class ReportTests(PostgresFixture postgres)
{
    private static readonly DateOnly Feb15 = new(2026, 2, 15);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Money Sek(decimal v) => new(v, "SEK");

    [Fact]
    public async Task The_portfolio_counts_running_loans_and_their_outstanding_principal_by_product_at_a_date()
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var accounts = new FakeAccounts(app, 1_000_000m);
        await DisbursementTests.DisburseAsync(app, (await app.ApprovedAsync("CAR", 200_000m, 36)).Id);
        await DisbursementTests.DisburseAsync(app, (await app.ApprovedAsync("CAR", 100_000m, 24)).Id);
        await DisbursementTests.DisburseAsync(app, (await app.ApprovedAsync("MORTGAGE", 2_000_000m, 360)).Id);
        await app.RunDayAsync(Feb15); // every first installment collected

        var before = await app.GetJsonAsync<PortfolioReport>($"/reports/portfolio?date={LendingApp.Today:yyyy-MM-dd}");
        var after = await app.GetJsonAsync<PortfolioReport>($"/reports/portfolio?date={Feb15:yyyy-MM-dd}");

        before.TotalLoans.ShouldBe(3);
        before.Products.Single(p => p.ProductCode == "CAR").ShouldBe(new PortfolioLine("CAR", 2, Sek(300_000m)));
        // On 15 Feb: CAR 200,000 − 5,012.52 and 100,000 − its first principal; MORTGAGE 2,000,000 − 2,755.47.
        after.Products.Single(p => p.ProductCode == "MORTGAGE").Outstanding.ShouldBe(Sek(1_997_244.53m));
        after.Products.Single(p => p.ProductCode == "CAR").Outstanding.Amount.ShouldBeLessThan(300_000m - 5_012.52m);
        accounts.Debits.Count.ShouldBe(3);
        await ProjectionsMatchTheLoansAsync(app);
    }

    [Fact]
    public async Task Arrears_group_loans_by_their_classification_on_the_date()
    {
        await using var app = await LendingApp.StartAsync(postgres);
        _ = new FakeAccounts(app, 0m);
        var loan = await DisbursementTests.DisburseAsync(app, (await app.ApprovedAsync()).Id);
        await app.RunDayAsync(Feb15.AddDays(1));   // Overdue
        await app.RunDayAsync(Feb15.AddDays(90));  // Default

        var onDay1 = await app.GetJsonAsync<ArrearsReport>($"/reports/arrears?date={Feb15:yyyy-MM-dd}");
        var onDay2 = await app.GetJsonAsync<ArrearsReport>($"/reports/arrears?date={Feb15.AddDays(1):yyyy-MM-dd}");
        var onDay90 = await app.GetJsonAsync<ArrearsReport>($"/reports/arrears?date={Feb15.AddDays(90):yyyy-MM-dd}");

        Count(onDay1, "Current").ShouldBe(1);
        Count(onDay2, "Overdue").ShouldBe(1);
        Count(onDay90, "Default").ShouldBe(1);
        onDay90.Classifications.Single(c => c.Classification == "Default").ShouldSatisfyAllConditions(
            c => c.Currency.ShouldBe("SEK"), c => c.Outstanding.ShouldBe(Sek(200_000m)));
        _ = loan;
        await ProjectionsMatchTheLoansAsync(app);
    }

    [Fact]
    public async Task A_payment_while_overdue_lowers_the_overdue_outstanding_from_its_date_only()
    {
        // CAR 200,000, item 1 (5,012.52 principal + 1,158.33 interest) unpaid → Overdue on 16 Feb. On 20 Feb a card pays
        // 3,000 for item 1: interest 1,158.33, principal 1,841.67, so Overdue outstanding = 200,000 − 1,841.67 = 198,158.33.
        await using var app = await LendingApp.StartAsync(postgres);
        _ = new FakeAccounts(app, 0m);
        var loan = await DisbursementTests.DisburseAsync(app, (await app.ApprovedAsync()).Id);
        await app.RunDayAsync(Feb15.AddDays(1));
        await app.MoveToAsync(Feb15.AddDays(5));
        await app.DeliverAsync(new Bank.Contracts.Events.CardPaymentCaptured(Guid.NewGuid(), Feb15.AddDays(5), "customer", Guid.NewGuid(), loan.Id, 1, Sek(3_000m)), loan.Id.ToString());

        var before = await app.GetJsonAsync<ArrearsReport>($"/reports/arrears?date={Feb15.AddDays(4):yyyy-MM-dd}");
        var after = await app.GetJsonAsync<ArrearsReport>($"/reports/arrears?date={Feb15.AddDays(5):yyyy-MM-dd}");

        before.Classifications.Single(c => c.Classification == "Overdue").Outstanding.ShouldBe(Sek(200_000m));
        after.Classifications.Single(c => c.Classification == "Overdue").Outstanding.ShouldBe(Sek(198_158.33m));
        Count(after, "Current").ShouldBe(0);
        await ProjectionsMatchTheLoansAsync(app);
    }

    [Fact]
    public async Task A_loan_paid_off_while_overdue_leaves_the_overdue_class_on_that_date()
    {
        // Overdue on 16 Feb; on 20 Feb a card pays more than the whole loan (prepaying every later item), so it closes as
        // PaidOff. The Closed row must leave Overdue, the class it was in, before the class is reset (ADR 0006).
        await using var app = await LendingApp.StartAsync(postgres);
        _ = new FakeAccounts(app, 0m);
        var loan = await DisbursementTests.DisburseAsync(app, (await app.ApprovedAsync()).Id);
        await app.RunDayAsync(Feb15.AddDays(1));
        await app.MoveToAsync(Feb15.AddDays(5));
        await app.DeliverAsync(new Bank.Contracts.Events.CardPaymentCaptured(Guid.NewGuid(), Feb15.AddDays(5), "customer", Guid.NewGuid(), loan.Id, null, Sek(300_000m)), loan.Id.ToString());

        var arrears = await app.GetJsonAsync<ArrearsReport>($"/reports/arrears?date={Feb15.AddDays(5):yyyy-MM-dd}");
        var portfolio = await app.GetJsonAsync<PortfolioReport>($"/reports/portfolio?date={Feb15.AddDays(5):yyyy-MM-dd}");

        arrears.Classifications.ShouldBeEmpty(); // no running loan, so no currency line at all
        portfolio.TotalLoans.ShouldBe(0);
        await ProjectionsMatchTheLoansAsync(app);
    }

    [Fact]
    public async Task A_paid_off_loan_leaves_the_portfolio_after_its_closing_date()
    {
        await using var app = await LendingApp.StartAsync(postgres);
        var accounts = new FakeAccounts(app, 1_000_000m);
        var loan = await DisbursementTests.DisburseAsync(app, (await app.ApprovedAsync()).Id);
        await app.MoveToAsync(Feb15.AddDays(-5));
        await app.CreateClient().PostAsync($"/loans/{loan.Id}/payoff", null, Ct);
        await app.OutboxDrainedAsync();

        (await app.GetJsonAsync<PortfolioReport>($"/reports/portfolio?date={Feb15.AddDays(-6):yyyy-MM-dd}")).TotalLoans.ShouldBe(1);
        (await app.GetJsonAsync<PortfolioReport>($"/reports/portfolio?date={Feb15.AddDays(-5):yyyy-MM-dd}")).TotalLoans.ShouldBe(0);
        (await app.GetJsonAsync<ArrearsReport>($"/reports/arrears?date={Feb15.AddDays(-5):yyyy-MM-dd}")).Classifications.ShouldBeEmpty();
        accounts.Debits.ShouldHaveSingleItem();
        await ProjectionsMatchTheLoansAsync(app);
    }

    [Fact]
    public async Task A_card_payment_while_a_payoff_is_pending_is_not_allocated_and_the_reports_stay_right()
    {
        // Two CAR loans of 200,000. On 10 Feb loan A asks for a payoff; before Accounts answers, a card pays 6,170.85 for it.
        // Majid, 2026-11-02: while a payoff is pending, a card payment allocates nothing and is owed back (ledger ADR 0002).
        // The payoff then settles the quoted principal once, so the portfolio holds only loan B's 200,000.
        await using var app = await LendingApp.StartAsync(postgres);
        var a = await DisbursementTests.DisburseAsync(app, (await app.ApprovedAsync()).Id);
        await DisbursementTests.DisburseAsync(app, (await app.ApprovedAsync()).Id);
        await app.MoveToAsync(Feb15.AddDays(-5));
        (await app.CreateClient().PostAsync($"/loans/{a.Id}/payoff", null, Ct)).EnsureSuccessStatusCode();
        await app.OutboxDrainedAsync();
        var payoff = app.Published<Bank.Contracts.Events.LoanPayoffRequested>().ShouldHaveSingleItem();

        await app.DeliverAsync(new Bank.Contracts.Events.CardPaymentCaptured(Guid.NewGuid(), Feb15.AddDays(-5), "customer", Guid.NewGuid(), a.Id, 1, Sek(6_170.85m)), a.Id.ToString());
        await app.DeliverAsync(new Bank.Contracts.Events.PaymentCollected(Guid.NewGuid(), Feb15.AddDays(-5), "system", a.Id, payoff.CollectionId, null, "Payoff", payoff.Amount), a.Id.ToString());
        await app.OutboxDrainedAsync();

        var card = app.Published<Bank.Contracts.Events.LoanPaymentAllocated>().Single(p => p.Source == "Card");
        card.Principal.ShouldBe(Sek(0m));
        card.Unallocated.ShouldBe(Sek(6_170.85m));
        var portfolio = await app.GetJsonAsync<PortfolioReport>($"/reports/portfolio?date={Feb15.AddDays(-5):yyyy-MM-dd}");
        portfolio.Products.ShouldHaveSingleItem().ShouldBe(new PortfolioLine("CAR", 1, Sek(200_000m)));
        await ProjectionsMatchTheLoansAsync(app);
    }

    [Fact]
    public async Task Reports_are_for_staff_only()
    {
        await using var app = await LendingApp.StartAsync(postgres);

        (await app.Client(customerId: app.CustomerId).GetAsync("/reports/portfolio", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// The projections' invariant (ADR 0006), per loan: a running loan sums to 1 loan and its unpaid principal, in its current
    /// classification only; a closed loan sums to 0 and 0. A forgotten ReportProjections call breaks it.
    /// </summary>
    private static async Task ProjectionsMatchTheLoansAsync(LendingApp app)
    {
        const string sql = """
            WITH unpaid AS (SELECT loan_id, sum(principal_due - paid_principal) AS principal FROM schedule_item GROUP BY loan_id),
                 expected AS (SELECT l.id, l.classification,
                                     CASE WHEN l.closed_on IS NULL THEN 1 ELSE 0 END AS loans,
                                     CASE WHEN l.closed_on IS NULL THEN coalesce(u.principal, 0) ELSE 0 END AS outstanding
                              FROM loan l LEFT JOIN unpaid u ON u.loan_id = l.id),
                 portfolio AS (SELECT loan_id, sum(loans) AS loans, sum(outstanding) AS outstanding FROM portfolio_movement GROUP BY loan_id),
                 arrears AS (SELECT loan_id, classification, sum(loans) AS loans, sum(outstanding) AS outstanding FROM arrears_movement GROUP BY loan_id, classification)
            SELECT count(*) FROM expected e
            WHERE NOT EXISTS (SELECT 1 FROM portfolio p WHERE p.loan_id = e.id AND p.loans = e.loans AND p.outstanding = e.outstanding)
               OR EXISTS (SELECT 1 FROM arrears a WHERE a.loan_id = e.id
                          AND (a.loans, a.outstanding) <> (CASE WHEN a.classification = e.classification THEN e.loans ELSE 0 END,
                                                           CASE WHEN a.classification = e.classification THEN e.outstanding ELSE 0 END))
               OR (e.loans = 1 AND NOT EXISTS (SELECT 1 FROM arrears a WHERE a.loan_id = e.id AND a.classification = e.classification))
            """;
        (await app.ScalarAsync<long>(sql)).ShouldBe(0, "every loan's projection rows must sum to its state");
    }

    private static int Count(ArrearsReport report, string classification) => report.Classifications.Where(c => c.Classification == classification).Sum(c => c.Loans);
}
