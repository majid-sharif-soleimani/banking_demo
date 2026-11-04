using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Bank.Agent.Mcp.Tests;

public class LoanFiguresTests
{
    private const string Token = "bank-token-123";

    private static readonly Guid Customer = Guid.Parse("7a1c2a0e-0000-4000-8000-000000000001");

    private static readonly Guid LoanId = Guid.Parse("0199a000-0000-7000-8000-0000000000aa");

    private static readonly DateOnly Today = new(2026, 12, 30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Money(string amount) => $$"""{"amount":"{{amount}}","currency":"SEK"}""";

    private static string Item(int number, string due, string principal, string interest, string penalty, string paidPrincipal, string paidInterest, string paidPenalty, string status) =>
        $$"""
        {"number":{{number}},"dueDate":"{{due}}","principalDue":{{Money(principal)}},"interestDue":{{Money(interest)}},"penaltyDue":{{Money(penalty)}},
         "paidPrincipal":{{Money(paidPrincipal)}},"paidInterest":{{Money(paidInterest)}},"paidPenalty":{{Money(paidPenalty)}},"status":"{{status}}"}
        """;

    // Item 1 is paid, 2 is overdue and partly paid, 3 is not due yet.
    private static readonly string Loans = $$"""
        [{"id":"{{LoanId}}","applicationId":"0199a000-0000-7000-8000-0000000000bb","customerId":"{{Customer}}","productCode":"CAR","principal":{{Money("10000.00")}},
          "repaymentAccountId":"0199a000-0000-7000-8000-0000000000cc","disbursedOn":"2026-09-01","status":"Active","classification":"Overdue",
          "outstandingPrincipal":{{Money("8000.00")}},"overdueAmount":{{Money("1100.10")}},"daysPastDue":60,
          "schedule":[
            {{Item(1, "2026-10-01", "1000.00", "100.00", "0", "1000.00", "100.00", "0", "Paid")}},
            {{Item(2, "2026-11-01", "1000.00", "90.00", "20.10", "0", "10.00", "0", "Unpaid")}},
            {{Item(3, "2027-01-01", "8000.00", "80.00", "0", "0", "0", "0", "Unpaid")}}]}]
        """;

    private static JsonElement Summary(Guid? loanId = null, int? installment = null)
    {
        using var document = JsonDocument.Parse(LoanFigures.Summarize(Loans, Today, loanId, installment));
        return document.RootElement.Clone();
    }

    private static decimal Amount(JsonElement money) => money.GetProperty("amount").GetDecimal();

    [Fact]
    public void The_counts_and_the_totals_come_from_the_schedule()
    {
        var loan = Summary()[0];

        loan.GetProperty("installments").GetProperty("total").GetInt32().ShouldBe(3);
        loan.GetProperty("installments").GetProperty("paid").GetInt32().ShouldBe(1);
        loan.GetProperty("installments").GetProperty("open").GetInt32().ShouldBe(2);
        loan.GetProperty("installments").GetProperty("overdue").GetInt32().ShouldBe(1);
        Amount(loan.GetProperty("paidSoFar").GetProperty("total")).ShouldBe(1110.00m);
        Amount(loan.GetProperty("unpaidOnSchedule").GetProperty("principal")).ShouldBe(9000.00m);
        Amount(loan.GetProperty("unpaidOnSchedule").GetProperty("total")).ShouldBe(9180.10m);
        loan.GetProperty("lastDueDate").GetString().ShouldBe("2027-01-01");
    }

    [Fact]
    public void The_bank_s_own_figures_are_passed_on_unchanged()
    {
        var loan = Summary()[0];

        Amount(loan.GetProperty("overdueAmount")).ShouldBe(1100.10m);
        Amount(loan.GetProperty("outstandingPrincipal")).ShouldBe(8000.00m);
        loan.GetProperty("daysPastDue").GetInt32().ShouldBe(60);
        loan.GetProperty("classification").GetString().ShouldBe("Overdue");
    }

    [Fact]
    public void The_oldest_open_and_the_next_not_due_are_told_apart()
    {
        var loan = Summary()[0];

        var oldest = loan.GetProperty("oldestOpen");
        oldest.GetProperty("number").GetInt32().ShouldBe(2);
        oldest.GetProperty("overdue").GetBoolean().ShouldBeTrue();
        Amount(oldest.GetProperty("due").GetProperty("total")).ShouldBe(1110.10m);
        Amount(oldest.GetProperty("unpaid").GetProperty("total")).ShouldBe(1100.10m);
        Amount(oldest.GetProperty("unpaid").GetProperty("interest")).ShouldBe(80.00m);
        loan.GetProperty("nextNotDue").GetProperty("number").GetInt32().ShouldBe(3);
    }

    [Fact]
    public void An_asked_installment_is_given_and_an_unknown_one_is_empty()
    {
        Summary(installment: 1)[0].GetProperty("asked").GetProperty("status").GetString().ShouldBe("Paid");
        Summary(installment: 99)[0].GetProperty("asked").ValueKind.ShouldBe(JsonValueKind.Null);
        Summary()[0].GetProperty("asked").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void One_loan_can_be_asked_for() =>
        Summary(Guid.NewGuid()).GetArrayLength().ShouldBe(0);

    [Fact]
    public void A_paid_off_loan_has_nothing_open()
    {
        var paid = $$"""[{"id":"{{LoanId}}","customerId":"{{Customer}}","productCode":"CAR","principal":{{Money("1000.00")}},"disbursedOn":"2026-09-01","status":"PaidOff","classification":"Current","outstandingPrincipal":{{Money("0")}},"overdueAmount":{{Money("0")}},"daysPastDue":0,"schedule":[{{Item(1, "2026-10-01", "1000.00", "100.00", "0", "1000.00", "100.00", "0", "Paid")}}]}]""";

        var loan = JsonDocument.Parse(LoanFigures.Summarize(paid, Today)).RootElement[0];

        loan.GetProperty("oldestOpen").ValueKind.ShouldBe(JsonValueKind.Null);
        loan.GetProperty("nextNotDue").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void The_summary_holds_no_schedule_and_stays_small_however_long_the_loan_is()
    {
        var items = string.Join(",", Enumerable.Range(1, 60).Select(n => Item(n, $"20{27 + (n / 12)}-{(n % 12) + 1:00}-01", "1000.00", "100.00", "0", "0", "0", "0", "Unpaid")));
        var loans = $$"""[{"id":"{{LoanId}}","customerId":"{{Customer}}","productCode":"CAR","principal":{{Money("60000.00")}},"disbursedOn":"2026-09-01","status":"Active","classification":"Current","outstandingPrincipal":{{Money("60000.00")}},"overdueAmount":{{Money("0")}},"daysPastDue":0,"schedule":[{{items}}]}]""";

        var summary = LoanFigures.Summarize(loans, Today);

        summary.ShouldNotContain("\"schedule\"");
        summary.Length.ShouldBeLessThan(loans.Length / 5);
    }

    [Fact]
    public async Task The_customer_tool_asks_for_the_loans_of_the_connection_and_the_business_date()
    {
        await using var app = new McpServerApp();
        app.Gateway.BodiesByPath[$"/api/lending/loans?customerId={Customer}"] = Loans;
        app.Gateway.BodiesByPath["/api/operations/business-days/current"] = """{"date":"2026-12-30","status":"Open"}""";
        await using var client = await app.ConnectAsync(Token, Customer);

        var result = await client.CallToolAsync("loan_summary", new Dictionary<string, object?> { ["installmentNumber"] = 2 }, cancellationToken: Ct);

        result.IsError.ShouldNotBe(true);
        var loan = JsonDocument.Parse(string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text))).RootElement[0];
        loan.GetProperty("asked").GetProperty("number").GetInt32().ShouldBe(2);
    }

    [Theory]
    [InlineData("customer_loan_summary", """{"customerId":"7a1c2a0e-0000-4000-8000-000000000001"}""", "/api/lending/loans?customerId=7a1c2a0e-0000-4000-8000-000000000001")]
    [InlineData("loans_by_classification", """{"classification":"Overdue"}""", "/api/lending/loans?classification=Overdue")]
    public async Task The_admin_tools_answer_without_schedules(string tool, string arguments, string path)
    {
        await using var app = new McpServerApp();
        app.Gateway.BodiesByPath[path] = Loans;
        app.Gateway.BodiesByPath["/api/operations/business-days/current"] = """{"date":"2026-12-30","status":"Open"}""";
        await using var client = await app.ConnectAdminAsync(Token);

        var result = await client.CallToolAsync(tool, JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments), cancellationToken: Ct);

        result.IsError.ShouldNotBe(true);
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        text.ShouldContain("oldestOpen");
        text.ShouldNotContain("\"schedule\"");
    }
}
