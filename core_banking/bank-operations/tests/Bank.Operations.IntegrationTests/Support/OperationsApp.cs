using System.Net.Http.Json;
using System.Text.Json;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Messaging;
using Bank.Messaging.Inbox;
using Bank.Messaging.Transport;
using Bank.Operations.Api.Data;
using Bank.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Bank.Operations.IntegrationTests.Support;

/// <summary>
/// The real Operations service (its Program.cs, endpoints, handlers and hosted services) on its own role and schema,
/// with an in-memory broker in place of Kafka. Outgoing events appear in <see cref="Broker"/> once the outbox dispatcher
/// sent them; incoming events are handed to the service's pipeline with <see cref="DeliverAsync{T}"/>.
/// </summary>
public sealed class OperationsApp : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> settings;

    private OperationsApp(OperationsDatabase database, Dictionary<string, string?> settings)
    {
        Database = database;
        this.settings = settings;
    }

    public OperationsDatabase Database { get; }

    public InMemoryTransport Broker => (InMemoryTransport)Services.GetRequiredService<IMessageTransport>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <param name="participants">Operations:EndOfDayParticipants; a single stub participant by default.</param>
    /// <param name="startDate">Operations:StartDate.</param>
    public static async Task<OperationsApp> StartAsync(
        PostgresFixture postgres, string[]? participants = null, DateOnly? startDate = null)
    {
        var database = await OperationsDatabase.CreateAsync(postgres);
        return StartOn(database, participants, startDate);
    }

    /// <summary>Starts the service on an existing schema, as a restart or a second instance would.</summary>
    public static OperationsApp StartOn(OperationsDatabase database, string[]? participants = null, DateOnly? startDate = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:operations-db"] = database.ConnectionString,
            // Kafka is replaced below; the transport's options still need a value to pass validation.
            ["ConnectionStrings:kafka"] = "localhost:1",
            ["Operations:StartDate"] = (startDate ?? new DateOnly(2026, 1, 1)).ToString("yyyy-MM-dd"),
        };
        var list = participants ?? ["stub"];
        for (var i = 0; i < list.Length; i++)
        {
            settings[$"Operations:EndOfDayParticipants:{i}"] = list[i];
        }

        var app = new OperationsApp(database, settings);
        _ = app.Server; // starts the host now, so startup work has run when the test continues
        return app;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting is visible to Program.cs while it builds (ConfigureAppConfiguration would come too late).
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // No Kafka: drop its consumer and topic initializer and send through an in-memory broker.
            foreach (var kafka in services.Where(IsKafkaHostedService).ToList())
            {
                services.Remove(kafka);
            }

            services.RemoveAll<IMessageTransport>();
            services.AddSingleton<IMessageTransport, InMemoryTransport>();
            services.PostConfigure<BankMessagingOptions>(o => o.PollingInterval = TimeSpan.FromMilliseconds(20));
        });
    }

    private static bool IsKafkaHostedService(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(IHostedService)
        && (descriptor.ImplementationType?.Assembly.GetName().Name == "Bank.Messaging.Kafka"
            || descriptor.ImplementationFactory?.Method.ReturnType.Assembly.GetName().Name == "Bank.Messaging.Kafka");

    /// <summary>Hands an event to the service as its Kafka consumer would, through the inbox pipeline.</summary>
    public Task DeliverAsync<T>(T @event, string schemaUri, string key) where T : IIntegrationEvent =>
        Services.GetRequiredService<MessagePipeline>().ProcessAsync(
            new TransportMessage(@event.EventId, schemaUri, key, JsonSerializer.Serialize(@event, ContractJson.Options),
                @event.PerformedBy, @event.OccurredOn),
            Ct);

    /// <summary>The events of one type the service has sent so far.</summary>
    public IReadOnlyList<T> Published<T>(string schemaUri) =>
        [.. Broker.Sent.Where(m => m.SchemaUri == schemaUri).Select(m => JsonSerializer.Deserialize<T>(m.Payload, ContractJson.Options)!)];

    /// <summary>Waits until the dispatcher has sent every outbox row, so "nothing more was published" can be asserted.</summary>
    public Task OutboxDrainedAsync() =>
        Eventually.AssertAsync(async () =>
            (await Database.ScalarAsync<long>("SELECT count(*) FROM outbox_message WHERE sent_at IS NULL")).ShouldBe(0));

    public async Task<HttpResponseMessage> PostAsync(string path, string? performedBy = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (performedBy is not null)
        {
            request.Headers.Add("X-Performed-By", performedBy);
        }

        return await CreateClient().SendAsync(request, Ct);
    }

    public async Task<T> GetJsonAsync<T>(string path) =>
        (await CreateClient().GetFromJsonAsync<T>(path, Ct))!;

    /// <summary>Adds a business day directly, for tests that need a state the API cannot reach in one step.</summary>
    public async Task AddBusinessDayAsync(DateOnly date, BusinessDayStatus status)
    {
        await using var context = Database.NewContext();
        context.BusinessDays.Add(new BusinessDay
        {
            Date = date,
            Status = status,
            OpenedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            PerformedBy = "tester",
        });
        await context.SaveChangesAsync(Ct);
    }
}
