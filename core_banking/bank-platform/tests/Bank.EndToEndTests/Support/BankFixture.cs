using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.EndToEndTests.Support;

/// <summary>
/// A fresh bank for one scenario: the real AppHost (six services, the gateway, PostgreSQL, Kafka, Mailpit) started once
/// per test class, so each scenario begins on Operations:StartDate with empty databases. Every call goes through the
/// gateway, as a client outside the bank would make it; the tests reference no service code.
/// </summary>
public sealed class BankFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(5);

    private DistributedApplication app = null!;
    private HttpClient gateway = null!;

    public HttpClient Mailpit { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(StartTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Bank_AppHost>(
            ["--DcpPublisher:RandomizePorts=true", "--DisableDashboard=true"], timeout.Token);
        app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        KeepLogs();
        await app.ResourceNotifications.WaitForResourceHealthyAsync("gateway", timeout.Token);

        gateway = app.CreateHttpClient("gateway");
        gateway.Timeout = TimeSpan.FromMinutes(2);
        Mailpit = new HttpClient { BaseAddress = app.GetEndpoint("mailpit", "http") };

        // The currencies are seeded inactive (bank-operations ADR 0002); a bank operator activates them first.
        foreach (var code in new[] { "SEK", "EUR", "USD" })
        {
            (await Staff.SendAsync(HttpMethod.Post, $"/api/operations/currencies/{code}/activate")).EnsureSuccessStatusCode();
        }
    }

    /// <summary>
    /// Every service's console log goes to e2e-logs/&lt;scenario&gt;/&lt;service&gt;.log next to the test binaries, so a failed
    /// scenario can be read afterwards (CI uploads the folder).
    /// </summary>
    private void KeepLogs()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "e2e-logs", $"{DateTime.UtcNow:HHmmss}-{Guid.NewGuid():N}"[..15]);
        Directory.CreateDirectory(folder);
        var logs = app.Services.GetRequiredService<ResourceLoggerService>();
        foreach (var resource in app.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<ProjectResource>())
        {
            _ = Task.Run(async () =>
            {
                await using var file = new StreamWriter(Path.Combine(folder, $"{resource.Name}.log")) { AutoFlush = true };
                await foreach (var batch in logs.WatchAsync(resource).WithCancellation(stopLogs.Token))
                {
                    foreach (var line in batch)
                    {
                        await file.WriteLineAsync(line.Content);
                    }
                }
            });
        }
    }

    private readonly CancellationTokenSource stopLogs = new();

    public async ValueTask DisposeAsync()
    {
        // Resource logs arrive a moment late; give the last lines (often the interesting ones) time to land.
        await Task.Delay(TimeSpan.FromSeconds(3));
        await stopLogs.CancelAsync();
        gateway?.Dispose();
        Mailpit?.Dispose();
        if (app is not null)
        {
            await app.DisposeAsync();
        }
    }

    /// <summary>Calls made by bank staff (X-Performed-By), such as reviews, approvals and closing the day.</summary>
    public Caller Staff => new(gateway, performedBy: "e2e-staff", customerId: null);

    /// <summary>Calls made by the customer themselves (X-Customer-Id): the services limit them to the customer's own data.</summary>
    public Caller Customer(Guid customerId) => new(gateway, performedBy: null, customerId);

    public async Task<DateOnly> TodayAsync() => (await Staff.GetAsync<BusinessDayView>("/api/operations/business-days/current")).Date;

    /// <summary>
    /// Closes the current business day and waits until the next one is open, which happens only after every participant
    /// (Accounts, Lending) completed its end of day. Effects those participants trigger through Kafka (collections,
    /// allocations, journal entries) may still arrive a moment later; checks on them use <see cref="Eventually"/>.
    /// </summary>
    public async Task CloseDayAsync()
    {
        var today = await TodayAsync();
        (await Staff.SendAsync(HttpMethod.Post, "/api/operations/business-days/current/close")).EnsureSuccessStatusCode();
        await Eventually.Async(async () =>
        {
            var day = await Staff.GetAsync<BusinessDayView>("/api/operations/business-days/current");
            return day.Date > today && day.Status == "Open";
        }, $"business day {today:yyyy-MM-dd} to close", TimeSpan.FromMinutes(2));
    }

    /// <summary>Closes days until the business date is <paramref name="date"/>.</summary>
    public async Task CloseDaysUntilAsync(DateOnly date)
    {
        while (await TodayAsync() < date)
        {
            await CloseDayAsync();
        }
    }

    /// <summary>The subjects of the emails Mailpit received for <paramref name="address"/>, oldest first.</summary>
    public async Task<IReadOnlyList<string>> EmailSubjectsAsync(string address)
    {
        // Mailpit pages results (50 by default, newest first); a long scenario sends one email per failed collection.
        var result = await Mailpit.GetFromJsonAsync<MailpitSearch>($"/api/v1/search?limit=1000&query={Uri.EscapeDataString($"to:\"{address}\"")}", Caller.Json);
        return [.. result!.Messages.Select(m => m.Subject).Reverse()];
    }

    private sealed record MailpitSearch(List<MailpitMessage> Messages);
    private sealed record MailpitMessage(string Subject);
}

/// <summary>One caller's view of the gateway: the identity headers it sends, and small JSON helpers.</summary>
public sealed class Caller(HttpClient gateway, string? performedBy, Guid? customerId)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (performedBy is not null)
        {
            request.Headers.Add("X-Performed-By", performedBy);
        }

        if (customerId is not null)
        {
            request.Headers.Add("X-Customer-Id", customerId.ToString());
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (body is HttpContent content)
        {
            request.Content = content;
        }
        else if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        var response = await gateway.SendAsync(request);
        await response.Content.LoadIntoBufferAsync();
        return response;
    }

    public async Task<T> GetAsync<T>(string path)
    {
        var response = await SendAsync(HttpMethod.Get, path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"GET {path}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    /// <summary>POST that must succeed (2xx); returns the body. Money moves get a fresh Idempotency-Key.</summary>
    public async Task<T> PostAsync<T>(string path, object? body = null, bool idempotent = false)
    {
        var response = await SendAsync(HttpMethod.Post, path, body ?? new { }, idempotent ? Guid.NewGuid().ToString() : null);
        response.IsSuccessStatusCode.ShouldBeTrue($"POST {path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    /// <summary>
    /// POST that may first be refused because the service has not yet received an event it depends on (a customer or
    /// account registered a moment ago in another service). Such answers are retried; any other answer counts.
    /// </summary>
    public async Task<T> PostWhenKnownAsync<T>(string path, object body, bool idempotent = false)
    {
        var key = idempotent ? Guid.NewGuid().ToString() : null;
        HttpResponseMessage response = null!;
        await Eventually.Async(async () =>
        {
            response = await SendAsync(HttpMethod.Post, path, body, key);
            var text = await response.Content.ReadAsStringAsync();
            return response.IsSuccessStatusCode || !NotYet.Any(code => text.Contains($"\"{code}\"", StringComparison.Ordinal));
        }, $"POST {path} to be accepted", TimeSpan.FromMinutes(1));
        response.IsSuccessStatusCode.ShouldBeTrue($"POST {path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static readonly string[] NotYet = ["customer_not_found", "currency_not_active", "repayment_account_invalid", "account_not_found", "loan_not_found", "application_not_found"];
}

public static class Eventually
{
    /// <summary>Polls until <paramref name="condition"/> holds; fails with <paramref name="what"/> after the timeout.</summary>
    public static async Task Async(Func<Task<bool>> condition, string what, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Waited in vain for {what}.");
            }

            await Task.Delay(200);
        }
    }

    /// <summary>Polls <paramref name="read"/> until the value satisfies <paramref name="condition"/>, and returns it.</summary>
    public static async Task<T> Value<T>(Func<Task<T>> read, Func<T, bool> condition, string what, TimeSpan? timeout = null)
    {
        T value = default!;
        await Async(async () => condition(value = await read()), what, timeout);
        return value;
    }
}
