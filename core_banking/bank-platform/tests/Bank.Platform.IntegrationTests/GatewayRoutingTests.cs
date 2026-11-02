using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.Testing;

namespace Bank.Platform.IntegrationTests;

/// <summary>The gateway in the real AppHost reaches every service through Aspire service discovery.</summary>
public sealed class GatewayRoutingTests(PlatformFixture platform)
{
    [Theory]
    [InlineData("/api/lending/loan-products", "PURCHASE")]
    [InlineData("/api/operations/currencies", "SEK")]
    [InlineData("/api/customers/openapi/v1.json", "openapi")]
    [InlineData("/api/accounts/openapi/v1.json", "openapi")]
    [InlineData("/api/cards/openapi/v1.json", "openapi")]
    [InlineData("/api/ledger/openapi/v1.json", "openapi")]
    public async Task Each_service_answers_through_the_gateway(string path, string expected)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await platform.App.ResourceNotifications.WaitForResourceHealthyAsync("gateway", timeout.Token);
        using var client = platform.App.CreateHttpClient("gateway");
        client.DefaultRequestHeaders.Add("X-Performed-By", "platform-test");

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
