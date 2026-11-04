using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Bank.Agent.Mcp.Tests;

public class CustomerChangeToolsTests
{
    private const string Token = "bank-token-123";

    private static readonly Guid Customer = Guid.Parse("7a1c2a0e-0000-4000-8000-000000000001");

    private static readonly Guid From = Guid.Parse("0199a000-0000-7000-8000-000000000001");

    private static readonly Guid To = Guid.Parse("0199a000-0000-7000-8000-000000000002");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, object?> Transfer => new()
    {
        ["fromAccountId"] = From, ["toAccountId"] = To, ["amount"] = 250.5m, ["currency"] = "SEK", ["description"] = "Rent",
    };

    [Fact]
    public async Task A_change_without_a_confirmation_key_is_refused_and_reaches_no_bank()
    {
        await using var app = new McpServerApp();
        await using var client = await app.ConnectAsync(Token, Customer);

        var result = await client.CallToolAsync("transfer_create", Transfer, cancellationToken: Ct);

        result.IsError.ShouldBe(true);
        string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text)).ShouldContain("confirmation");
        app.Gateway.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_confirmed_transfer_sends_the_key_and_money_as_the_bank_takes_it()
    {
        await using var app = new McpServerApp();
        await using var client = await app.ConnectAsync(Token, Customer, idempotencyKey: "change-1");

        var result = await client.CallToolAsync("transfer_create", Transfer, cancellationToken: Ct);

        result.IsError.ShouldNotBe(true);
        app.Gateway.Requests.ShouldHaveSingleItem().PathAndQuery.ShouldBe("/api/accounts/transfers");
        var (method, key, body) = app.Gateway.Sent.ShouldHaveSingleItem();
        method.ShouldBe(HttpMethod.Post);
        key.ShouldBe("change-1");
        var json = JsonDocument.Parse(body!).RootElement;
        json.GetProperty("amount").GetProperty("amount").GetString().ShouldBe("250.5");
        json.GetProperty("amount").GetProperty("currency").GetString().ShouldBe("SEK");
        json.GetProperty("toAccountId").GetGuid().ShouldBe(To);
    }

    [Fact]
    public async Task An_application_is_submitted_for_the_connection_customer()
    {
        await using var app = new McpServerApp();
        await using var client = await app.ConnectAsync(Token, Customer, idempotencyKey: "change-2");

        await client.CallToolAsync("loan_application_submit", new Dictionary<string, object?>
        {
            ["productCode"] = "CAR", ["amount"] = 100000m, ["currency"] = "SEK", ["termMonths"] = 36,
            ["repaymentAccountId"] = From, ["purpose"] = "A car",
        }, cancellationToken: Ct);

        JsonDocument.Parse(app.Gateway.Sent.Single().Body!).RootElement.GetProperty("customerId").GetGuid().ShouldBe(Customer);
    }

    [Fact]
    public async Task Optional_arguments_may_be_left_out()
    {
        // Found in the live check: a standing order without an end date failed in argument binding.
        await using var app = new McpServerApp();
        await using var client = await app.ConnectAsync(Token, Customer, idempotencyKey: "change-4");

        var result = await client.CallToolAsync("standing_order_create", new Dictionary<string, object?>
        {
            ["fromAccountId"] = From, ["toAccountId"] = To, ["amount"] = 10m, ["currency"] = "SEK", ["frequency"] = "Monthly", ["startDate"] = "2026-01-01",
        }, cancellationToken: Ct);

        result.IsError.ShouldNotBe(true);
        JsonDocument.Parse(app.Gateway.Sent.Single().Body!).RootElement.GetProperty("endDate").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_cancel_is_a_delete_of_the_customers_order()
    {
        await using var app = new McpServerApp();
        await using var client = await app.ConnectAsync(Token, Customer, idempotencyKey: "change-3");
        var order = Guid.NewGuid();

        await client.CallToolAsync("standing_order_cancel", new Dictionary<string, object?> { ["standingOrderId"] = order }, cancellationToken: Ct);

        app.Gateway.Sent.Single().Method.ShouldBe(HttpMethod.Delete);
        app.Gateway.Requests.Single().PathAndQuery.ShouldBe($"/api/accounts/standing-orders/{order}");
    }

    [Fact]
    public async Task An_uploaded_document_goes_to_the_customers_interactions_as_a_multipart_upload()
    {
        await using var app = new McpServerApp();
        await using var client = await app.ConnectAsync(Token, Customer, idempotencyKey: "upload-1");
        var application = Guid.NewGuid();

        await client.CallToolAsync("host_document_upload", new Dictionary<string, object?>
        {
            ["applicationId"] = application, ["fileName"] = "payslip.pdf", ["contentType"] = "application/pdf",
            ["contentBase64"] = Convert.ToBase64String("%PDF-1.4 test"u8.ToArray()),
        }, cancellationToken: Ct);

        app.Gateway.Requests.Single().PathAndQuery.ShouldBe($"/api/customers/customers/{Customer}/interactions");
        var body = app.Gateway.Sent.Single().Body!;
        body.ShouldContain("Upload");
        body.ShouldContain(application.ToString());
        body.ShouldContain("payslip.pdf");
        body.ShouldContain("%PDF-1.4 test");
    }

    [Fact]
    public async Task A_simulation_is_a_read_and_needs_no_confirmation()
    {
        await using var app = new McpServerApp();
        await using var client = await app.ConnectAsync(Token, Customer);

        var result = await client.CallToolAsync("loan_simulation", new Dictionary<string, object?>
        {
            ["productCode"] = "CAR", ["amount"] = 100000m, ["currency"] = "SEK", ["termMonths"] = 36,
        }, cancellationToken: Ct);

        result.IsError.ShouldNotBe(true);
        app.Gateway.Requests.Single().PathAndQuery.ShouldBe("/api/lending/loan-products/CAR/simulation");
        app.Gateway.Sent.Single().IdempotencyKey.ShouldBeNull();
    }
}
