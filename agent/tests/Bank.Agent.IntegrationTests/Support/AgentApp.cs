using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Bank.Agent.Host.Features.Agent;
using Bank.Agent.Host.Features.Auth;
using Bank.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace Bank.Agent.IntegrationTests.Support;

/// <summary>
/// The real agent host (its Program.cs, endpoints and migrations) on a fresh "agent" schema, with
/// <see cref="StubGateway"/> in place of the gateway and a clock the test moves by hand.
/// </summary>
public sealed class AgentApp(string connectionString, IChatClient? model = null) : WebApplicationFactory<Program>
{
    public StubGateway Gateway { get; } = new();

    /// <summary>The bank tools every run gets; the real ones need the MCP server.</summary>
    public FakeBankTools BankTools { get; } = new();

    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));

    public string ConnectionString { get; } = connectionString;

    public IReadOnlyCollection<string> LogLines =>
        Services.GetServices<ILoggerProvider>().OfType<CapturingLoggerProvider>().Single().Lines;

    /// <summary>Starts the host; without a model, the echo answers (no model key in tests).</summary>
    public static async Task<AgentApp> StartAsync(PostgresFixture postgres, IChatClient? model = null)
    {
        var app = new AgentApp(await postgres.CreateServiceSchemaAsync("agent"), model);
        _ = app.Server;
        return app;
    }

    /// <summary>A new process on the same schema, as after a restart of the host.</summary>
    public static AgentApp StartOn(string connectionString)
    {
        var app = new AgentApp(connectionString);
        _ = app.Server;
        return app;
    }

    public Task SweepAsync() =>
        Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<SessionSweeper>().Single().SweepOnceAsync(TestContext.Current.CancellationToken);

    /// <summary>A browser of its own: its own cookie, nothing shared with another browser.</summary>
    public Browser NewBrowser() => new(CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false }));

    /// <summary>Every text value in every table of the schema, to look for secrets that must not be there.</summary>
    public async Task<string> DumpSchemaTextAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        var text = new StringBuilder();
        foreach (var table in new[] { "chat", "chat_message", "__EFMigrationsHistory" })
        {
            await using var command = new NpgsqlCommand($"SELECT row_to_json(t)::text FROM \"{table}\" t", connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                text.AppendLine(reader.GetString(0));
            }
        }

        return text.ToString();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:agent-db", ConnectionString);
        builder.UseSetting("Agent:Gateway:ApiKey", StubGateway.ApiKey);
        builder.UseSetting("Agent:IdleTimeout", "00:05:00");
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            foreach (var json in configuration.Sources.OfType<JsonConfigurationSource>().ToList())
            {
                configuration.Sources.Remove(json);
            }
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddHttpClient<BankGateway>().ConfigurePrimaryHttpMessageHandler(() => Gateway);
            services.RemoveAll<IBankTools>();
            services.AddSingleton<IBankTools>(BankTools);
            if (model is not null)
            {
                services.RemoveAll<IChatClient>();
                services.AddSingleton(model);
            }
            // Every level reaches this provider, whatever the Logging section says.
            services.AddSingleton<ILoggerProvider, CapturingLoggerProvider>();
            services.AddLogging(logging => logging.AddFilter<CapturingLoggerProvider>(null, LogLevel.Trace));
        });
    }
}

public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}

/// <summary>One browser tab: keeps the session cookie by hand and sends the chat UI's header unless told not to.</summary>
public sealed class Browser(HttpClient client)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string? Cookie { get; private set; }

    public string? LastSetCookie { get; private set; }

    public async Task<HttpResponseMessage> PostAsync(string path, object? body = null, bool withClientHeader = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body ?? new { }) };
        return await SendAsync(request, withClientHeader);
    }

    public async Task<HttpResponseMessage> GetAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync(request, withClientHeader: true);
    }

    public async Task<HttpResponseMessage> PostFileAsync(string path, byte[] content, string fileName, string contentType)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new MultipartFormDataContent { { file, "file", fileName } } };
        return await SendAsync(request, withClientHeader: true);
    }

    public Task<HttpResponseMessage> LoginAsync(string username, string password) => PostAsync("/auth/login", new { username, password });

    public async Task<JsonElement> LoginOkAsync(string username, string password)
    {
        using var response = await LoginAsync(username, password);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    /// <summary>One AG-UI run with the given messages (by default one user message); returns the events in order.</summary>
    public async Task<List<JsonElement>> RunAsync(string text, object[]? messages = null)
    {
        var body = new
        {
            threadId = "client-thread",
            runId = Guid.NewGuid().ToString(),
            state = new { },
            messages = messages ?? [new { id = "m1", role = "user", content = text }],
            tools = Array.Empty<object>(),
            context = Array.Empty<object>(),
            forwardedProps = new { },
        };
        using var response = await PostAsync("/agent", body);
        response.EnsureSuccessStatusCode();
        var events = new List<JsonElement>();
        foreach (var line in (await response.Content.ReadAsStringAsync()).Split('\n'))
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                events.Add(JsonDocument.Parse(line["data: ".Length..]).RootElement.Clone());
            }
        }

        return events;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, bool withClientHeader)
    {
        if (withClientHeader)
        {
            request.Headers.Add(ChatClientHeader.Name, "1");
        }

        if (Cookie is not null)
        {
            request.Headers.Add("Cookie", $"{SessionCookie.Name}={Cookie}");
        }

        var response = await client.SendAsync(request);
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.FirstOrDefault(c => c.StartsWith(SessionCookie.Name + "=", StringComparison.Ordinal)) is { } set)
        {
            LastSetCookie = set;
            var value = set[(SessionCookie.Name.Length + 1)..].Split(';')[0];
            Cookie = string.IsNullOrEmpty(value) ? null : value;
        }

        return response;
    }
}
