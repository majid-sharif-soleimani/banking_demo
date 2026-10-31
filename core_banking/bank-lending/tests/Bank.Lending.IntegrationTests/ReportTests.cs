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
        onDay90.Classifications.Single(c => c.Classification == "Default").Outstanding.ShouldBe(Sek(200_000m));
        _ = loan;
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
        accounts.Debits.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Reports_are_for_staff_only()
    {
        await using var app = await LendingApp.StartAsync(postgres);

        (await app.Client(customerId: app.CustomerId).GetAsync("/reports/portfolio", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static int Count(ArrearsReport report, string classification) => report.Classifications.Single(c => c.Classification == classification).Loans;
}
