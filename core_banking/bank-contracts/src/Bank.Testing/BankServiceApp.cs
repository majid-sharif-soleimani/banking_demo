using System.Net.Http.Json;
using System.Text.Json;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Contracts.Schema;
using Bank.Messaging;
using Bank.Messaging.Inbox;
using Bank.Messaging.Transport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace Bank.Testing;

/// <summary>
/// A real service (its Program.cs, endpoints, handlers, hosted services and migrations) running in memory on its own
/// database schema, with an <see cref="InMemoryTransport"/> instead of Kafka. Outgoing events appear in
/// <see cref="Broker"/> once the outbox dispatcher has sent them; incoming events go through the service's inbox
/// pipeline with <see cref="DeliverAsync{T}"/>. The service's appsettings*.json files are not loaded, because
/// configuration arrays merge by index: the test sets every value it needs in the constructor's settings.
/// </summary>
public class BankServiceApp<TProgram> : WebApplicationFactory<TProgram>
    where TProgram : class
{
    private readonly IReadOnlyDictionary<string, string?> settings;
    private readonly Action<IServiceCollection>? configureServices;

    /// <param name="connectionString">The service's role, from <see cref="PostgresFixture.CreateServiceSchemaAsync"/>.</param>
    /// <param name="settings">Configuration, such as ConnectionStrings:accounts-db; ConnectionStrings:kafka is added.</param>
    /// <param name="configureServices">Test doubles, such as an in-memory email sender.</param>
    public BankServiceApp(string connectionString, IDictionary<string, string?> settings, Action<IServiceCollection>? configureServices = null)
    {
        ConnectionString = connectionString;
        this.settings = new Dictionary<string, string?>(settings)
        {
            // Kafka is replaced below; its options still need a value to pass validation.
            ["ConnectionStrings:kafka"] = "localhost:1",
        };
        this.configureServices = configureServices;
    }

    public string ConnectionString { get; }

    public InMemoryTransport Broker => (InMemoryTransport)Services.GetRequiredService<IMessageTransport>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Starts the host now, so startup work (migrations, hosted services) has run when the test continues.</summary>
    public void Start() => _ = Server;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting is visible to Program.cs while it builds (ConfigureAppConfiguration would come too late for that).
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

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
            foreach (var kafka in services.Where(IsKafkaHostedService).ToList())
            {
                services.Remove(kafka);
            }

            services.RemoveAll<IMessageTransport>();
            services.AddSingleton<IMessageTransport, InMemoryTransport>();
            services.PostConfigure<BankMessagingOptions>(o => o.PollingInterval = TimeSpan.FromMilliseconds(20));
            configureServices?.Invoke(services);
        });
    }

    private static bool IsKafkaHostedService(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(IHostedService)
        && (descriptor.ImplementationType?.Assembly.GetName().Name == "Bank.Messaging.Kafka"
            || descriptor.ImplementationFactory?.Method.ReturnType.Assembly.GetName().Name == "Bank.Messaging.Kafka");

    /// <summary>Hands an event to the service as its Kafka consumer would, through the inbox pipeline.</summary>
    public Task<ProcessOutcome> DeliverAsync<T>(T @event, string key, CancellationToken? ct = null) where T : IIntegrationEvent =>
        Services.GetRequiredService<MessagePipeline>().ProcessAsync(
            new TransportMessage(@event.EventId, SchemaOf<T>(), key, JsonSerializer.Serialize(@event, ContractJson.Options),
                @event.PerformedBy, @event.OccurredOn),
            ct ?? Ct);

    /// <summary>Delivers BusinessDayOpened, so a service with the business-day consumer knows today's date.</summary>
    public Task<ProcessOutcome> OpenBusinessDayAsync(DateOnly date) =>
        DeliverAsync(new BusinessDayOpened(Guid.NewGuid(), date, "system", date), date.ToString("yyyy-MM-dd"));

    /// <summary>The events of one type the service has sent so far.</summary>
    public IReadOnlyList<T> Published<T>() where T : IIntegrationEvent =>
        [.. Broker.Sent.Where(m => m.SchemaUri == SchemaOf<T>()).Select(m => JsonSerializer.Deserialize<T>(m.Payload, ContractJson.Options)!)];

    /// <summary>Waits until the dispatcher has sent every outbox row, so "nothing more was published" can be asserted.</summary>
    public Task OutboxDrainedAsync() =>
        Eventually.AssertAsync(async () =>
        {
            var unsent = await ScalarAsync<long>("SELECT count(*) FROM outbox_message WHERE sent_at IS NULL");
            if (unsent != 0)
            {
                throw new InvalidOperationException($"{unsent} outbox rows are not sent yet.");
            }
        });

    /// <summary>Reads with plain SQL, outside EF, so the test sees what is really committed.</summary>
    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }

    /// <summary>A client with the bank's headers: X-Performed-By, X-Customer-Id and Idempotency-Key when given.</summary>
    public HttpClient Client(string? performedBy = null, Guid? customerId = null, string? idempotencyKey = null)
    {
        var client = CreateClient();
        if (performedBy is not null)
        {
            client.DefaultRequestHeaders.Add("X-Performed-By", performedBy);
        }

        if (customerId is not null)
        {
            client.DefaultRequestHeaders.Add("X-Customer-Id", customerId.Value.ToString());
        }

        if (idempotencyKey is not null)
        {
            client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        }

        return client;
    }

    public Task<HttpResponseMessage> PostJsonAsync<TBody>(string path, TBody body, string? idempotencyKey = null, string? performedBy = null) =>
        Client(performedBy, idempotencyKey: idempotencyKey).PostAsJsonAsync(path, body, Ct);

    public async Task<T> GetJsonAsync<T>(string path) => (await CreateClient().GetFromJsonAsync<T>(path, Ct))!;

    private static string SchemaOf<T>() =>
        EventSchemaUri.Find(typeof(T)) ?? throw new InvalidOperationException($"{typeof(T).Name} declares no SchemaUri.");
}
