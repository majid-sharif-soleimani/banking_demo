using System.Net;
using ModelContextProtocol.Protocol;

namespace Bank.Agent.Mcp.Tests;

public class CustomerToolsTests
{
    private const string Token = "bank-token-123";

    private static readonly Guid Customer = Guid.Parse("7a1c2a0e-0000-4000-8000-000000000001");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));

    [Fact]
    public async Task The_customer_endpoint_lists_only_the_customer_read_tools()
    {
        await using var app = new McpServerApp();
        await using var client = await app.ConnectAsync(Token, Customer);

        var tools = await client.ListToolsAsync(cancellationToken: Ct);

        tools.Where(t => t.ProtocolTool.Annotations!.ReadOnlyHint == true).Select(t => t.Name).Order().ShouldBe([
            "account_statement", "accounts_list", "appointment_slots", "appointments_list", "business_date", "loan_applications_list",
            "loan_products_list", "loan_simulation", "loans_list", "payoff_quote", "standing_orders_list"]);
        tools.Where(t => t.ProtocolTool.Annotations!.ReadOnlyHint == false).Select(t => t.Name).Order().ShouldBe([
            "appointment_book", "appointment_cancel", "card_payment", "host_document_upload", "loan_application_submit", "payoff_request",
            "standing_order_cancel", "standing_order_create", "transfer_create"]);
    }

    [Theory]
    [InlineData("business_date", "/api/operations/business-days/current")]
    [InlineData("accounts_list", "/api/accounts/accounts?customerId=7a1c2a0e-0000-4000-8000-000000000001")]
    [InlineData("loans_list", "/api/lending/loans?customerId=7a1c2a0e-0000-4000-8000-000000000001")]
    [InlineData("loan_products_list", "/api/lending/loan-products")]
    [InlineData("standing_orders_list", "/api/accounts/standing-orders?customerId=7a1c2a0e-0000-4000-8000-000000000001")]
    [InlineData("appointments_list", "/api/customers/appointments?customerId=7a1c2a0e-0000-4000-8000-000000000001")]
    [InlineData("loan_applications_list", "/api/lending/loan-applications")]
    public async Task A_tool_calls_its_bank_path_with_the_token_and_the_key_and_returns_the_bank_json(string tool, string path)
    {
        await using var app = new McpServerApp();
        app.Gateway.Body = """{"balance":{"amount":10.50,"currency":"SEK"}}""";
        await using var client = await app.ConnectAsync(Token, Customer);

        var result = await client.CallToolAsync(tool, cancellationToken: Ct);

        result.IsError.ShouldNotBe(true);
        Text(result).ShouldBe(app.Gateway.Body);
        app.Gateway.Requests.ShouldHaveSingleItem().ShouldBe((path, $"Bearer {Token}", McpServerApp.ApiKey));
    }

    [Fact]
    public async Task The_statement_tool_passes_the_account_and_the_period()
    {
        await using var app = new McpServerApp();
        await using var client = await app.ConnectAsync(Token, Customer);
        var account = Guid.Parse("0199a000-0000-7000-8000-000000000042");

        await client.CallToolAsync("account_statement",
            new Dictionary<string, object?> { ["accountId"] = account, ["from"] = "2026-10-01", ["to"] = "2026-10-31" }, cancellationToken: Ct);

        app.Gateway.Requests.ShouldHaveSingleItem().PathAndQuery
            .ShouldBe($"/api/accounts/accounts/{account}/statement?from=2026-10-01&to=2026-10-31");
    }

    [Fact]
    public async Task An_ended_bank_session_comes_back_as_a_tool_error_the_model_can_explain()
    {
        await using var app = new McpServerApp();
        app.Gateway.Status = HttpStatusCode.Unauthorized;
        await using var client = await app.ConnectAsync(Token, Customer);

        var result = await client.CallToolAsync("accounts_list", cancellationToken: Ct);

        result.IsError.ShouldBe(true);
        Text(result).ShouldContain("session has ended");
    }

    [Fact]
    public async Task A_bank_refusal_comes_back_as_a_tool_error_with_the_bank_answer()
    {
        await using var app = new McpServerApp();
        app.Gateway.Status = HttpStatusCode.BadRequest;
        app.Gateway.Body = """{"code":"date_in_future"}""";
        await using var client = await app.ConnectAsync(Token, Customer);

        var result = await client.CallToolAsync("loan_products_list", cancellationToken: Ct);

        result.IsError.ShouldBe(true);
        Text(result).ShouldContain("date_in_future");
    }

    [Fact]
    public async Task Without_a_token_no_bank_call_is_made()
    {
        await using var app = new McpServerApp();
        await using var client = await app.ConnectAsync(token: null, Customer);

        var result = await client.CallToolAsync("loan_products_list", cancellationToken: Ct);

        result.IsError.ShouldBe(true);
        app.Gateway.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_customer_id_the_customer_tools_make_no_bank_call()
    {
        await using var app = new McpServerApp();
        await using var client = await app.ConnectAsync(Token, customerId: null);

        var result = await client.CallToolAsync("accounts_list", cancellationToken: Ct);

        result.IsError.ShouldBe(true);
        app.Gateway.Requests.ShouldBeEmpty();
    }
}
