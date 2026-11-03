using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bank.Agent.IntegrationTests.Support;
using Bank.Testing;
using Npgsql;

namespace Bank.Agent.IntegrationTests;

public class AuthTests(PostgresFixture postgres)
{
    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var code) ? code.GetString() : null;

    private static async Task<(string Status, string? Reason)> ChatRow(AgentApp app, Guid chatId)
    {
        await using var connection = new NpgsqlConnection(app.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT status, closed_reason FROM chat WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", chatId);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    [Fact]
    public async Task The_migration_creates_the_chat_tables_in_the_agent_schema()
    {
        await using var app = await AgentApp.StartAsync(postgres);
        await using var connection = new NpgsqlConnection(app.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT string_agg(table_name, ',' ORDER BY table_name) FROM information_schema.tables WHERE table_schema = current_schema()", connection);

        ((string)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!).ShouldBe("__EFMigrationsHistory,chat,chat_message");
    }

    [Fact]
    public async Task A_login_opens_a_new_chat_and_sets_a_cookie_scripts_cannot_read()
    {
        await using var app = await AgentApp.StartAsync(postgres);
        var browser = app.NewBrowser();

        var info = await browser.LoginOkAsync(StubGateway.TesterUsername, StubGateway.TesterPassword);

        info.GetProperty("role").GetString().ShouldBe("customer");
        info.GetProperty("name").GetString().ShouldBe("Tess Tester");
        info.TryGetProperty("token", out _).ShouldBeFalse();
        var setCookie = browser.LastSetCookie!.ToLowerInvariant();
        setCookie.ShouldContain("httponly");
        setCookie.ShouldContain("secure");
        setCookie.ShouldContain("samesite=strict");
        setCookie.ShouldContain("path=/");
        (await ChatRow(app, info.GetProperty("chatId").GetGuid())).ShouldBe(("Open", null));
        app.Gateway.Requests.ShouldAllBe(r => r.ApiKey == StubGateway.ApiKey);
    }

    [Theory]
    [InlineData(StubGateway.TesterUsername, "wrong")]
    [InlineData("NOBODY", "NOBODY")]
    public async Task A_wrong_login_gets_one_answer_for_every_cause(string username, string password)
    {
        await using var app = await AgentApp.StartAsync(postgres);
        var browser = app.NewBrowser();

        using var response = await browser.LoginAsync(username, password);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeOf(response)).ShouldBe("invalid_credentials");
        browser.Cookie.ShouldBeNull();
    }

    [Fact]
    public async Task Blank_fields_are_refused_without_asking_the_bank()
    {
        await using var app = await AgentApp.StartAsync(postgres);

        using var response = await app.NewBrowser().LoginAsync(" ", "");

        (await CodeOf(response)).ShouldBe("invalid_credentials");
        app.Gateway.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_bank_that_does_not_answer_is_503()
    {
        await using var app = await AgentApp.StartAsync(postgres);
        app.Gateway.Down = true;

        using var response = await app.NewBrowser().LoginAsync("admin", "admin");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await CodeOf(response)).ShouldBe("bank_unavailable");
    }

    [Fact]
    public async Task Me_tells_the_ui_who_is_logged_in()
    {
        await using var app = await AgentApp.StartAsync(postgres);
        var browser = app.NewBrowser();
        var login = await browser.LoginOkAsync("admin", "admin");

        var me = await (await browser.GetAsync("/auth/me")).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        me.GetProperty("role").GetString().ShouldBe("admin");
        me.GetProperty("name").GetString().ShouldBe("Admin");
        me.GetProperty("chatId").GetGuid().ShouldBe(login.GetProperty("chatId").GetGuid());
    }

    [Fact]
    public async Task Without_a_cookie_there_is_no_session()
    {
        await using var app = await AgentApp.StartAsync(postgres);

        using var response = await app.NewBrowser().GetAsync("/auth/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeOf(response)).ShouldBe("missing_session");
    }

    [Fact]
    public async Task A_login_elsewhere_closes_the_first_chat_and_the_first_browser_learns_why()
    {
        await using var app = await AgentApp.StartAsync(postgres);
        var first = app.NewBrowser();
        var firstChat = (await first.LoginOkAsync("IMP-C001", "IMP-C001")).GetProperty("chatId").GetGuid();

        var secondChat = (await app.NewBrowser().LoginOkAsync("IMP-C001", "IMP-C001")).GetProperty("chatId").GetGuid();

        secondChat.ShouldNotBe(firstChat);
        (await ChatRow(app, firstChat)).ShouldBe(("Closed", "new_login"));
        using var me = await first.GetAsync("/auth/me");
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeOf(me)).ShouldBe("missing_session");
    }

    [Fact]
    public async Task A_second_login_in_the_same_browser_opens_a_new_chat()
    {
        await using var app = await AgentApp.StartAsync(postgres);
        var browser = app.NewBrowser();
        var firstChat = (await browser.LoginOkAsync("admin", "admin")).GetProperty("chatId").GetGuid();

        var secondChat = (await browser.LoginOkAsync(StubGateway.TesterUsername, StubGateway.TesterPassword)).GetProperty("chatId").GetGuid();

        (await ChatRow(app, firstChat)).ShouldBe(("Closed", "new_login"));
        (await ChatRow(app, secondChat)).ShouldBe(("Open", null));
    }

    [Fact]
    public async Task Logout_closes_the_chat_ends_the_bank_session_and_clears_the_cookie()
    {
        await using var app = await AgentApp.StartAsync(postgres);
        var browser = app.NewBrowser();
        var chatId = (await browser.LoginOkAsync("admin", "admin")).GetProperty("chatId").GetGuid();

        using var response = await browser.PostAsync("/auth/logout");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        browser.Cookie.ShouldBeNull();
        (await ChatRow(app, chatId)).ShouldBe(("Closed", "logout"));
        app.Gateway.Requests.ShouldContain(r => r.Path == "/api/identity/logout");
    }

    [Fact]
    public async Task A_host_session_unused_for_the_idle_timeout_ends_and_closes_its_chat()
    {
        await using var app = await AgentApp.StartAsync(postgres);
        var browser = app.NewBrowser();
        var chatId = (await browser.LoginOkAsync("admin", "admin")).GetProperty("chatId").GetGuid();

        app.Clock.Advance(TimeSpan.FromMinutes(5));
        using var response = await browser.GetAsync("/auth/me");

        (await CodeOf(response)).ShouldBe("session_expired");
        (await ChatRow(app, chatId)).ShouldBe(("Closed", "session_expired"));
    }

    [Theory]
    [InlineData("/auth/login")]
    [InlineData("/auth/logout")]
    [InlineData("/agent")]
    public async Task Calls_without_the_chat_ui_header_are_refused(string path)
    {
        await using var app = await AgentApp.StartAsync(postgres);

        using var response = await app.NewBrowser().PostAsync(path, new { username = "admin", password = "admin" }, withClientHeader: false);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOf(response)).ShouldBe("missing_client_header");
        app.Gateway.Requests.ShouldBeEmpty();
    }
}
