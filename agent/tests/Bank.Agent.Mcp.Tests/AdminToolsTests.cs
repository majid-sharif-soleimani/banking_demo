using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Bank.Agent.Mcp.Tests;

public class AdminToolsTests
{
    private const string Token = "admin-token-1";

    private static readonly Guid Customer = Guid.Parse("7a1c2a0e-0000-4000-8000-000000000001");

    private static readonly Guid Application = Guid.Parse("5b2d0000-0000-4000-8000-000000000002");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));

    [Fact]
    public async Task The_admin_endpoint_lists_the_admin_tools_and_none_of_the_customer_tools()
    {
        await using var app = new McpServerApp();
        await using var client = await app.ConnectAdminAsync(Token);

        var tools = await client.ListToolsAsync(cancellationToken: Ct);

        tools.Where(t => t.ProtocolTool.Annotations!.ReadOnlyHint == true).Select(t => t.Name).Order().ShouldBe([
            "appointment_slots", "arrears_report", "business_date", "currencies_list", "customer_accounts", "customer_appointments",
            "customer_get", "customer_interactions", "customer_loan_summary", "customer_loans", "customer_standing_orders", "customers_search",
            "host_customer_document", "host_customers_report", "information_requests_of_application", "information_requests_open", "journal_entries", "loan_application_get",
            "loan_applications_queue", "loan_products_list", "loans_by_classification", "portfolio_report", "trial_balance"]);
        tools.Where(t => t.ProtocolTool.Annotations!.ReadOnlyHint == false).Select(t => t.Name).Order().ShouldBe([
            "appointment_book_for_customer", "business_day_close", "currency_activate", "customer_note_add", "information_request_complete",
            "loan_application_approve", "loan_application_disburse", "loan_application_reject", "loan_application_request_information",
            "loan_application_start_review"]);
    }

    [Fact]
    public async Task The_customer_endpoint_still_has_its_own_tools_after_an_admin_request()
    {
        await using var app = new McpServerApp();
        await using (var admin = await app.ConnectAdminAsync(Token))
        {
            await admin.ListToolsAsync(cancellationToken: Ct);
        }

        await using var customer = await app.ConnectAsync(Token, Customer);
        var names = (await customer.ListToolsAsync(cancellationToken: Ct)).Select(t => t.Name).ToList();

        names.ShouldContain("accounts_list");
        names.ShouldNotContain("trial_balance");
    }

    [Fact]
    public async Task A_customer_connection_cannot_call_an_admin_tool()
    {
        await using var app = new McpServerApp();
        await using var client = await app.ConnectAsync(Token, Customer);

        await Should.ThrowAsync<ModelContextProtocol.McpException>(() =>
            client.CallToolAsync("trial_balance", new Dictionary<string, object?> { ["currency"] = "SEK" }, cancellationToken: Ct).AsTask());
        app.Gateway.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("currencies_list", "{}", "/api/operations/currencies")]
    [InlineData("trial_balance", """{"currency":"SEK"}""", "/api/ledger/trial-balance?currency=SEK")]
    [InlineData("trial_balance", """{"currency":"SEK","date":"2026-03-02"}""", "/api/ledger/trial-balance?currency=SEK&date=2026-03-02")]
    [InlineData("portfolio_report", "{}", "/api/lending/reports/portfolio")]
    [InlineData("arrears_report", """{"date":"2026-03-02"}""", "/api/lending/reports/arrears?date=2026-03-02")]
    [InlineData("customer_loans", """{"customerId":"7a1c2a0e-0000-4000-8000-000000000001"}""", "/api/lending/loans?customerId=7a1c2a0e-0000-4000-8000-000000000001")]
    [InlineData("customer_interactions", """{"customerId":"7a1c2a0e-0000-4000-8000-000000000001"}""", "/api/customers/customers/7a1c2a0e-0000-4000-8000-000000000001/interactions")]
    [InlineData("loan_applications_queue", """{"statuses":["Submitted","UnderReview"]}""", "/api/lending/loan-applications?status=Submitted&status=UnderReview&limit=20")]
    [InlineData("information_requests_open", "{}", "/api/customers/information-requests")]
    public async Task An_admin_read_calls_its_bank_path_with_the_admin_token(string tool, string arguments, string path)
    {
        await using var app = new McpServerApp();
        await using var client = await app.ConnectAdminAsync(Token);

        var result = await client.CallToolAsync(tool,
            System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments), cancellationToken: Ct);

        result.IsError.ShouldNotBe(true, Text(result));
        app.Gateway.Requests.ShouldHaveSingleItem().ShouldBe((path, $"Bearer {Token}", McpServerApp.ApiKey));
    }

    [Fact]
    public async Task The_customers_report_reads_every_page_and_only_what_is_asked_for()
    {
        await using var app = new McpServerApp();
        var id = Guid.NewGuid();
        app.Gateway.BodiesByPath["/api/customers/customers/search"] = $$"""{"items":[{"id":"{{id}}","name":"Ada","nationalId":"IMP-1","email":"a@example.com","phone":"070","status":"Active"}],"nextCursor":null}""";
        app.Gateway.BodiesByPath["/api/accounts/accounts"] = """[{"number":"111","type":"Current","balance":{"amount":1,"currency":"SEK"}}]""";
        await using var client = await app.ConnectAdminAsync(Token);

        var report = Text(await client.CallToolAsync("host_customers_report", new Dictionary<string, object?> { ["includeAccounts"] = true, ["includeLoans"] = false }, cancellationToken: Ct));

        using var json = JsonDocument.Parse(report);
        var customer = json.RootElement.GetProperty("customers").EnumerateArray().Single();
        customer.GetProperty("nationalId").GetString().ShouldBe("IMP-1");
        customer.GetProperty("accounts")[0].GetProperty("number").GetString().ShouldBe("111");
        customer.GetProperty("accounts")[0].TryGetProperty("balance", out _).ShouldBeFalse();
        customer.GetProperty("loans").GetArrayLength().ShouldBe(0);
        json.RootElement.GetProperty("more").GetBoolean().ShouldBeFalse();
        app.Gateway.Requests.ShouldNotContain(r => r.PathAndQuery.StartsWith("/api/lending/"));
    }

    [Fact]
    public async Task A_customer_search_without_a_filter_sends_none_so_the_bank_lists_every_customer()
    {
        await using var app = new McpServerApp();
        await using var client = await app.ConnectAdminAsync(Token);

        await client.CallToolAsync("customers_search", new Dictionary<string, object?>(), cancellationToken: Ct);

        var body = app.Gateway.Sent.ShouldHaveSingleItem().Body!;
        body.ShouldContain("\"name\":null");
        body.ShouldContain("\"nationalId\":null");
    }

    [Fact]
    public async Task A_customer_search_can_filter_on_the_national_id()
    {
        await using var app = new McpServerApp();
        await using var client = await app.ConnectAdminAsync(Token);

        await client.CallToolAsync("customers_search", new Dictionary<string, object?> { ["nationalId"] = "IMP-C001" }, cancellationToken: Ct);

        app.Gateway.Sent.ShouldHaveSingleItem().Body!.ShouldContain("\"nationalId\":\"IMP-C001\"");
    }

    [Fact]
    public async Task A_customer_search_is_a_post_so_the_filter_stays_out_of_the_url()
    {
        await using var app = new McpServerApp();
        await using var client = await app.ConnectAdminAsync(Token);

        await client.CallToolAsync("customers_search", new Dictionary<string, object?> { ["name"] = "Berg" }, cancellationToken: Ct);

        app.Gateway.Requests.ShouldHaveSingleItem().PathAndQuery.ShouldBe("/api/customers/customers/search");
        var sent = app.Gateway.Sent.ShouldHaveSingleItem();
        sent.Method.ShouldBe(HttpMethod.Post);
        sent.Body!.ShouldContain("\"name\":\"Berg\"");
    }

    [Fact]
    public async Task An_admin_change_without_a_confirmation_key_is_refused()
    {
        await using var app = new McpServerApp();
        await using var client = await app.ConnectAdminAsync(Token);

        var result = await client.CallToolAsync("loan_application_approve",
            new Dictionary<string, object?> { ["applicationId"] = Application }, cancellationToken: Ct);

        result.IsError.ShouldBe(true);
        app.Gateway.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_confirmed_rejection_sends_the_reason_and_the_key()
    {
        await using var app = new McpServerApp();
        await using var client = await app.ConnectAdminAsync(Token, idempotencyKey: "confirm-9");

        await client.CallToolAsync("loan_application_reject",
            new Dictionary<string, object?> { ["applicationId"] = Application, ["reason"] = "Income too low" }, cancellationToken: Ct);

        app.Gateway.Requests.ShouldHaveSingleItem().PathAndQuery.ShouldBe($"/api/lending/loan-applications/{Application}/reject");
        var sent = app.Gateway.Sent.ShouldHaveSingleItem();
        sent.IdempotencyKey.ShouldBe("confirm-9");
        sent.Body.ShouldBe("""{"reason":"Income too low"}""");
    }

    [Fact]
    public async Task A_document_comes_back_as_base64_for_the_host()
    {
        await using var app = new McpServerApp();
        app.Gateway.Body = "%PDF-1.7 payslip";
        await using var client = await app.ConnectAdminAsync(Token);
        var interaction = Guid.NewGuid();

        var result = await client.CallToolAsync("host_customer_document",
            new Dictionary<string, object?> { ["customerId"] = Customer, ["interactionId"] = interaction }, cancellationToken: Ct);

        app.Gateway.Requests.ShouldHaveSingleItem().ShouldBe(
            ($"/api/customers/customers/{Customer}/interactions/{interaction}/file", $"Bearer {Token}", McpServerApp.ApiKey));
        var json = System.Text.Json.JsonDocument.Parse(Text(result)).RootElement;
        Convert.FromBase64String(json.GetProperty("contentBase64").GetString()!).ShouldBe("%PDF-1.7 payslip"u8.ToArray());
    }

    [Fact]
    public async Task A_confirmed_note_is_a_multipart_note_on_the_customers_history()
    {
        await using var app = new McpServerApp();
        await using var client = await app.ConnectAdminAsync(Token, idempotencyKey: "confirm-10");

        await client.CallToolAsync("customer_note_add",
            new Dictionary<string, object?> { ["customerId"] = Customer, ["text"] = "Called about the overdue installment." }, cancellationToken: Ct);

        app.Gateway.Requests.ShouldHaveSingleItem().PathAndQuery.ShouldBe($"/api/customers/customers/{Customer}/interactions");
        var body = app.Gateway.Sent.ShouldHaveSingleItem().Body!;
        body.ShouldContain("name=kind");
        body.ShouldContain("Note");
        body.ShouldContain("Called about the overdue installment.");
    }
}
