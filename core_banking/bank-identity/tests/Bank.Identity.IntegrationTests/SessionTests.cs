using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Bank.Identity.Api.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Identity.IntegrationTests;

/// <summary>A clock the test moves by hand, so the idle timeout is tested without waiting.</summary>
public sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// The real identity service (WebApplicationFactory) in front of a stub Customers service on a random port, which knows
/// two national ids and records what each lookup sent. Service discovery finds it through services:customers:http:0.
/// </summary>
public sealed class SessionTests : IAsyncLifetime
{
    private static readonly Guid Sara = Guid.Parse("01a1a147-0c9b-72e5-9d91-fcd6b923773e");

    private WebApplication customers = null!;
    private WebApplicationFactory<Program> identity = null!;
    private readonly TestClock clock = new();
    private readonly List<(string? PerformedBy, string Body)> lookups = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        customers = builder.Build();
        customers.MapPost("/customers/search", async (HttpRequest r) =>
        {
            var body = await new StreamReader(r.Body).ReadToEndAsync();
            lookups.Add((r.Headers["X-Performed-By"].ToString(), body));
            var nationalId = JsonDocument.Parse(body).RootElement.GetProperty("nationalId").GetString();
            object[] items = nationalId == "IMP-C001" ? [new { id = Sara, name = "Sara Lindqvist", email = "sara@example.com", phone = "1", status = "Active" }] : [];
            return Results.Ok(new { items, nextCursor = (string?)null });
        });
        await customers.StartAsync(Ct);
        var address = customers.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        identity = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("services:customers:http:0", address);
            host.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock));
        });
    }

    public async ValueTask DisposeAsync()
    {
        await identity.DisposeAsync();
        await customers.DisposeAsync();
    }

    private async Task<HttpResponseMessage> LoginAsync(string username, string password) =>
        await identity.CreateClient().PostAsJsonAsync("/login", new { username, password }, Ct);

    private async Task<LoginResponse> LoggedInAsync(string username, string password)
    {
        var response = await LoginAsync(username, password);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>(Ct))!;
    }

    private async Task<HttpResponseMessage> CurrentAsync(string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/sessions/current");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await identity.CreateClient().SendAsync(request, Ct);
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("code").GetString()!;

    [Fact]
    public async Task Admin_logs_in_and_the_token_stands_for_the_admin()
    {
        var login = await LoggedInAsync("admin", "admin");

        login.ShouldSatisfyAllConditions(l => l.Role.ShouldBe("admin"), l => l.Username.ShouldBe("admin"), l => l.CustomerId.ShouldBeNull(),
            l => l.ExpiresAt.ShouldBe(clock.Now.AddMinutes(5)), l => l.Token.Length.ShouldBeGreaterThanOrEqualTo(43));
        var session = (await (await CurrentAsync(login.Token)).Content.ReadFromJsonAsync<SessionView>(Ct))!;
        session.Role.ShouldBe("admin");
        lookups.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_customer_logs_in_with_the_national_id_and_the_token_stands_for_that_customer()
    {
        var login = await LoggedInAsync("IMP-C001", "IMP-C001");

        login.ShouldSatisfyAllConditions(l => l.Role.ShouldBe("customer"), l => l.CustomerId.ShouldBe(Sara), l => l.Name.ShouldBe("Sara Lindqvist"));
        (await (await CurrentAsync(login.Token)).Content.ReadFromJsonAsync<SessionView>(Ct))!.CustomerId.ShouldBe(Sara);
        // The lookup names an actor (the staff search needs one) and carries the national id in the body only.
        lookups.ShouldHaveSingleItem().ShouldSatisfyAllConditions(l => l.PerformedBy.ShouldBe("identity"), l => l.Body.ShouldContain("IMP-C001"));
    }

    [Theory]
    [InlineData("admin", "wrong")]
    [InlineData("IMP-C001", "IMP-C002")]
    [InlineData("IMP-C999", "IMP-C999")]
    [InlineData("user", "user")]
    [InlineData("", "")]
    public async Task Wrong_credentials_and_unknown_national_ids_get_the_same_401(string username, string password)
    {
        var response = await LoginAsync(username, password);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(response)).ShouldBe("invalid_credentials");
    }

    [Fact]
    public async Task Every_login_gets_a_new_token()
    {
        var tokens = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            tokens.Add((await LoggedInAsync("admin", "admin")).Token);
        }

        tokens.Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public async Task A_second_login_ends_the_first_session_which_hears_why_on_its_next_request()
    {
        var first = await LoggedInAsync("IMP-C001", "IMP-C001");
        var second = await LoggedInAsync("IMP-C001", "IMP-C001");

        var replaced = await CurrentAsync(first.Token);
        replaced.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(replaced)).ShouldBe("session_replaced");
        (await replaced.Content.ReadAsStringAsync(Ct)).ShouldContain("Someone logged in with your username.");
        // Said once; after that the old token is simply unknown.
        (await CodeAsync(await CurrentAsync(first.Token))).ShouldBe("invalid_token");
        (await CurrentAsync(second.Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sessions_of_different_users_do_not_replace_each_other()
    {
        var admin = await LoggedInAsync("admin", "admin");
        await LoggedInAsync("IMP-C001", "IMP-C001");

        (await CurrentAsync(admin.Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_session_expires_after_five_minutes_without_a_request()
    {
        var login = await LoggedInAsync("admin", "admin");

        clock.Now = clock.Now.AddMinutes(5);
        var expired = await CurrentAsync(login.Token);

        expired.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(expired)).ShouldBe("session_expired");
    }

    [Fact]
    public async Task Every_request_restarts_the_idle_timeout()
    {
        var login = await LoggedInAsync("admin", "admin");

        // Requests at 10:04 and 10:08: the session never sat idle for 5 minutes, so at 10:12 it is still valid.
        foreach (var _ in Enumerable.Range(0, 3))
        {
            clock.Now = clock.Now.AddMinutes(4);
            (await CurrentAsync(login.Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var session = (await (await CurrentAsync(login.Token)).Content.ReadFromJsonAsync<SessionView>(Ct))!;
        session.ExpiresAt.ShouldBe(clock.Now.AddMinutes(5));
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        var login = await LoggedInAsync("admin", "admin");
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/logout");
        logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);

        (await identity.CreateClient().SendAsync(logout, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await CodeAsync(await CurrentAsync(login.Token))).ShouldBe("invalid_token");
    }

    [Fact]
    public async Task A_request_without_a_token_is_401()
    {
        var response = await CurrentAsync(null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(response)).ShouldBe("missing_token");
        (await CodeAsync(await CurrentAsync("made-up"))).ShouldBe("invalid_token");
    }

    [Fact]
    public async Task A_customer_login_while_customers_is_down_is_503()
    {
        await customers.StopAsync(Ct);

        var response = await LoginAsync("IMP-C001", "IMP-C001");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await CodeAsync(response)).ShouldBe("customers_unavailable");
        // The admin does not need Customers.
        (await LoginAsync("admin", "admin")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
