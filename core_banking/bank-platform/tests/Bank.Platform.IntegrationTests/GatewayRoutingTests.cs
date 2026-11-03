using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;

namespace Bank.Platform.IntegrationTests;

/// <summary>The gateway in the real AppHost reaches every service through Aspire service discovery, after a login.</summary>
public sealed class GatewayRoutingTests(PlatformFixture platform)
{
    private sealed record Login(string Token);

    /// <summary>Logs in as admin through the gateway (workspace ADR 0007) and sends the token on every call.</summary>
    private static async Task<HttpClient> AdminAsync(PlatformFixture platform, CancellationToken ct)
    {
        var client = platform.App.CreateHttpClient("gateway");
        var response = await client.PostAsJsonAsync("/api/identity/login", new { username = "admin", password = "admin" }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<Login>(ct))!.Token);
        return client;
    }

    [Theory]
    [InlineData("/api/lending/loan-products", "PURCHASE")]
    [InlineData("/api/operations/currencies", "SEK")]
    [InlineData("/api/customers/openapi/v1.json", "openapi")]
    [InlineData("/api/accounts/openapi/v1.json", "openapi")]
    [InlineData("/api/cards/openapi/v1.json", "openapi")]
    [InlineData("/api/ledger/openapi/v1.json", "openapi")]
    [InlineData("/api/identity/sessions/current", "admin")]
    public async Task Each_service_answers_through_the_gateway(string path, string expected)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await platform.App.ResourceNotifications.WaitForResourceHealthyAsync("gateway", timeout.Token);
        using var client = await AdminAsync(platform, timeout.Token);

        var response = await client.GetAsync(path, timeout.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(timeout.Token)).ShouldContain(expected);
        response.Headers.Contains("X-Correlation-Id").ShouldBeTrue();
    }

    [Fact]
    public async Task The_gateway_refuses_anonymous_calls()
    {
        using var client = platform.App.CreateHttpClient("gateway");

        (await client.GetAsync("/api/lending/loan-products", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
