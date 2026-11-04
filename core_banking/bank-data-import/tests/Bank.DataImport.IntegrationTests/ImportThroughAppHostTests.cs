using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Bank.DataImport.Source;
using Microsoft.Extensions.Configuration;

namespace Bank.DataImport.IntegrationTests;

/// <summary>
/// Stage 7's integration test: a 5-customer synthetic fixture imported through the gateway of the real AppHost (all six
/// services, PostgreSQL and Kafka), day by day with the real end-of-day cycle, then verified.
/// </summary>
public sealed class ImportThroughAppHostTests
{
    [Fact]
    public async Task A_five_customer_fixture_imports_and_passes_verification()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Bank_AppHost>(
            ["--DcpPublisher:RandomizePorts=true", "--DisableDashboard=true"], timeout.Token);
        await using var app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("gateway", timeout.Token);

        var source = CsvSource.Load(Path.Combine(AppContext.BaseDirectory, "Fixture"), new ConfigurationBuilder().Build());
        using var gateway = app.CreateHttpClient("gateway");
        gateway.Timeout = TimeSpan.FromMinutes(2);
        var importer = new Importer(gateway, new ImportOptions(Customers: 100, DayTimeout: TimeSpan.FromMinutes(2)), TextWriter.Null);

        var report = await importer.RunAsync(source, timeout.Token);

        var markdown = report.ToMarkdown();
        report.VerificationErrors.ShouldBeEmpty(markdown);
        report.Customers.ShouldBe(5, markdown);
        report.Accounts.ShouldBe(5, markdown);
        report.Days.ShouldBeGreaterThanOrEqualTo(3);
        report.Applied.GetValueOrDefault("deposits").ShouldBe(5, markdown);
        report.Applied.GetValueOrDefault("transfers").ShouldBe(1, markdown);
        report.Applied.GetValueOrDefault("external transfers").ShouldBe(1, markdown);
        report.Applied.GetValueOrDefault("loans (CAR)").ShouldBe(1, markdown);
        report.Applied.GetValueOrDefault("loans (PURCHASE)").ShouldBe(1, markdown);
        report.Applied.GetValueOrDefault("standing orders").ShouldBe(1, markdown);
        report.Skipped.ShouldContain(s => s.StartsWith("transaction T7") && s.Contains("insufficient_funds"));
        report.Skipped.ShouldContain(s => s.StartsWith("loan L3") && s.Contains("amount_out_of_range"));
        report.ToMarkdown().ShouldContain("Verification: passed");
        report.Selection.ShouldContain("with a loan: 3 (target 30) — BELOW TARGET: the dataset has too few inside the window");

        // A second run is refused before it writes anything or closes a day.
        var dayBefore = await gateway.GetStringAsync("/api/operations/business-days/current", timeout.Token);
        var again = new Importer(gateway, new ImportOptions(Customers: 100, DayTimeout: TimeSpan.FromMinutes(2)), TextWriter.Null);
        var refused = await Should.ThrowAsync<InvalidOperationException>(() => again.RunAsync(source, timeout.Token));
        refused.Message.ShouldContain("already exists");
        again.Report.Customers.ShouldBe(0);
        (await gateway.GetStringAsync("/api/operations/business-days/current", timeout.Token)).ShouldBe(dayBefore);
    }
}
