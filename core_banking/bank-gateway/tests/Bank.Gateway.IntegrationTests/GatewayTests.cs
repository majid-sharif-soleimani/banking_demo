using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Gateway.IntegrationTests;

/// <summary>
/// The real gateway (WebApplicationFactory) in front of a real stub service on a random port, which echoes what it
/// received. Service discovery finds the stub through configuration, exactly as Aspire's services:&lt;name&gt;:http:0.
/// </summary>
public sealed class GatewayTests : IAsyncLifetime
{
    private const string AdminToken = "admin-token";
    private const string CustomerToken = "customer-token";
    private static readonly Guid CustomerId = Guid.NewGuid();

    private WebApplication stub = null!;
    private int checks;
    private WebApplicationFactory<Program> gateway = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        stub = builder.Build();
        stub.MapGet("/health", () => "Healthy");
        // The identity service's token check, with one token per case.
        stub.MapGet("/sessions/current", (HttpRequest r) =>
        {
            checks++;
            return r.Headers.Authorization.ToString() switch
            {
                "Bearer " + AdminToken => Results.Ok(new { username = "admin", role = "admin", customerId = (Guid?)null }),
                "Bearer " + CustomerToken => Results.Ok(new { username = "IMP-C001", role = "customer", customerId = (Guid?)CustomerId }),
                "Bearer replaced" => Results.Problem(statusCode: 401, title: "Someone logged in with your username.",
                    extensions: new Dictionary<string, object?> { ["code"] = "session_replaced" }),
                _ => Results.Problem(statusCode: 401, title: "Unknown token.", extensions: new Dictionary<string, object?> { ["code"] = "invalid_token" }),
            };
        });
        stub.Map("/{**path}", (HttpRequest r) => Results.Ok(new Echo(
            r.Path, r.Method, r.Headers["X-Customer-Id"].ToString(), r.Headers["X-Performed-By"].ToString(), r.Headers["X-Correlation-Id"].ToString(),
            r.Headers["X-Api-Key"].ToString(), r.Headers.Authorization.ToString())));
        await stub.StartAsync(Ct);
        var address = stub.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        gateway = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("Gateway:Services:0", "lending");
            host.UseSetting("Gateway:Services:1", "down");
            host.UseSetting("Gateway:Services:2", "identity");
            // appsettings.json lists seven services; arrays merge by index, so blank out the rest.
            for (var i = 3; i < 7; i++)
            {
                host.UseSetting($"Gateway:Services:{i}", "");
            }

            host.UseSetting("services:lending:http:0", address);
            host.UseSetting("services:down:http:0", "http://127.0.0.1:1");
            // The same stub plays the identity service.
            host.UseSetting("services:identity:http:0", address);
        });
    }

    public async ValueTask DisposeAsync()
    {
        await gateway.DisposeAsync();
        await stub.DisposeAsync();
    }

    private HttpClient Client(string? token = AdminToken, string? customer = null, string? performedBy = null, string? correlation = null)
    {
        var client = gateway.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        if (customer is not null) client.DefaultRequestHeaders.Add("X-Customer-Id", customer);
        if (performedBy is not null) client.DefaultRequestHeaders.Add("X-Performed-By", performedBy);
        if (correlation is not null) client.DefaultRequestHeaders.Add("X-Correlation-Id", correlation);
        return client;
    }

    [Fact]
    public async Task A_request_is_routed_to_its_service_without_the_api_prefix_as_the_admin()
    {
        var response = await Client().GetAsync("/api/lending/loans?customerId=x", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var echo = (await response.Content.ReadFromJsonAsync<Echo>(Ct))!;
        echo.Path.ShouldBe("/loans");
        echo.PerformedBy.ShouldBe("admin");
        echo.Customer.ShouldBeEmpty();
        Guid.TryParse(echo.Correlation, out _).ShouldBeTrue();
        response.Headers.GetValues("X-Correlation-Id").Single().ShouldBe(echo.Correlation);
    }

    [Fact]
    public async Task A_customer_token_becomes_the_customer_id_header()
    {
        var echo = await (await Client(CustomerToken).GetAsync("/api/lending/loans", Ct)).Content.ReadFromJsonAsync<Echo>(Ct);

        echo!.ShouldSatisfyAllConditions(e => e.Customer.ShouldBe(CustomerId.ToString()), e => e.PerformedBy.ShouldBeEmpty());
    }

    [Fact]
    public async Task Identity_headers_from_the_caller_are_replaced_by_the_session()
    {
        // A customer's token cannot claim staff, and nobody can claim another customer.
        var echo = await (await Client(CustomerToken, customer: Guid.NewGuid().ToString(), performedBy: "admin")
            .GetAsync("/api/lending/loans", Ct)).Content.ReadFromJsonAsync<Echo>(Ct);

        echo!.ShouldSatisfyAllConditions(e => e.Customer.ShouldBe(CustomerId.ToString()), e => e.PerformedBy.ShouldBeEmpty());
    }

    [Fact]
    public async Task The_token_is_not_forwarded_to_the_service() =>
        (await (await Client().GetAsync("/api/lending/loans", Ct)).Content.ReadFromJsonAsync<Echo>(Ct))!.Authorization.ShouldBeEmpty();

    [Fact]
    public async Task A_request_without_a_token_is_401_without_asking_identity()
    {
        var response = await Client(token: null, performedBy: "import").GetAsync("/api/lending/loan-products", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("missing_token");
        checks.ShouldBe(0);
    }

    [Theory]
    [InlineData("replaced", "session_replaced")]
    [InlineData("made-up", "invalid_token")]
    public async Task A_refused_token_gets_the_identity_services_own_401(string token, string code)
    {
        var response = await Client(token).GetAsync("/api/lending/loan-products", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain(code);
    }

    [Fact]
    public async Task Login_reaches_the_identity_service_without_a_token()
    {
        var response = await Client(token: null).PostAsync("/api/identity/login", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<Echo>(Ct))!.Path.ShouldBe("/login");
        checks.ShouldBe(0);
    }

    [Fact]
    public async Task When_identity_is_down_a_request_is_503()
    {
        using var client = gateway.WithWebHostBuilder(host => host.UseSetting("services:identity:http:0", "http://127.0.0.1:1")).CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {AdminToken}");

        var response = await client.GetAsync("/api/lending/loans", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("identity_unavailable");
    }

    [Fact]
    public async Task A_given_correlation_id_is_kept()
    {
        var echo = await (await Client(correlation: "trace-42").PostAsync("/api/lending/loan-applications", null, Ct))
            .Content.ReadFromJsonAsync<Echo>(Ct);

        echo!.ShouldSatisfyAllConditions(e => e.Correlation.ShouldBe("trace-42"), e => e.Method.ShouldBe("POST"));
    }

    [Theory]
    [InlineData("has spaces and <script>")]
    [InlineData("a-very-long-id-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task An_unsafe_correlation_id_is_replaced(string given)
    {
        var echo = await (await Client(correlation: given).GetAsync("/api/lending/loans", Ct)).Content.ReadFromJsonAsync<Echo>(Ct);

        Guid.TryParse(echo!.Correlation, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task The_openapi_index_lists_each_services_document()
    {
        var index = await gateway.CreateClient().GetFromJsonAsync<List<Dictionary<string, string>>>("/openapi", Ct);

        index!.Select(i => i["url"]).ShouldBe(["/api/lending/openapi/v1.json", "/api/down/openapi/v1.json", "/api/identity/openapi/v1.json"]);
    }

    [Fact]
    public async Task Health_includes_the_downstream_services()
    {
        // "down" points nowhere, so the aggregate is unhealthy; /alive checks only the gateway itself.
        (await gateway.CreateClient().GetAsync("/health", Ct)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await gateway.CreateClient().GetAsync("/alive", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // A fixed test value, not a real secret (.claude/rules/secrets.md).
    private WebApplicationFactory<Program> WithApiKey() => gateway.WithWebHostBuilder(host => host.UseSetting("Gateway:ApiKey", "test-key"));

    [Fact]
    public async Task With_an_api_key_set_a_call_without_it_is_401()
    {
        using var client = WithApiKey().CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {AdminToken}");

        var response = await client.GetAsync("/api/lending/loan-products", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("invalid_api_key");
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("test-key-but-longer")]
    [InlineData("")]
    public async Task A_wrong_api_key_is_401(string given)
    {
        using var client = WithApiKey().CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {AdminToken}");
        client.DefaultRequestHeaders.Add("X-Api-Key", given);

        (await client.GetAsync("/api/lending/loan-products", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_right_api_key_passes_and_is_not_forwarded_to_the_service()
    {
        using var client = WithApiKey().CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {AdminToken}");
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");

        var response = await client.GetAsync("/api/lending/loan-products", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var echo = (await response.Content.ReadFromJsonAsync<Echo>(Ct))!;
        echo.PerformedBy.ShouldBe("admin");
        echo.ApiKey.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_an_api_key_set_health_still_needs_none() =>
        (await WithApiKey().CreateClient().GetAsync("/alive", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

    [Fact]
    public void Outside_development_the_gateway_refuses_to_start_without_an_api_key()
    {
        var production = gateway.WithWebHostBuilder(host => host.UseEnvironment("Production"));

        Should.Throw<InvalidOperationException>(() => production.CreateClient()).Message.ShouldContain("Gateway:ApiKey");
    }

    [Fact]
    public void The_gateway_refuses_to_start_without_the_identity_service()
    {
        var withoutIdentity = gateway.WithWebHostBuilder(host => host.UseSetting("Gateway:Services:2", ""));

        Should.Throw<InvalidOperationException>(() => withoutIdentity.CreateClient()).Message.ShouldContain("identity");
    }

    private sealed record Echo(string Path, string Method, string Customer, string PerformedBy, string Correlation, string ApiKey, string Authorization);
}
