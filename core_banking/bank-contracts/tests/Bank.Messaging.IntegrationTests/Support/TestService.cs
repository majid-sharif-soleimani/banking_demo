using System.Text.Json;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Messaging.Inbox;
using Bank.Messaging.Outbox;
using Bank.Messaging.Transport;
using Bank.Testing;
using Bank.Toolkit.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Bank.Messaging.IntegrationTests.Support;

/// <summary>
/// A stand-in for one service: its own role and schema, a real host with its DbContext, migrations on startup and AddBankMessaging,
/// and its own in-memory outbound broker. Wire two of them with <c>sender.Outbound.Subscribe(receiver.Pipeline.ProcessAsync)</c>.
/// </summary>
public sealed class TestService : IAsyncDisposable
{
    private readonly IHost host;
    private readonly string connectionString;

    private TestService(IHost host, string connectionString)
    {
        this.host = host;
        this.connectionString = connectionString;
    }

    public IServiceProvider Services => host.Services;

    public InMemoryTransport Outbound => (InMemoryTransport)host.Services.GetRequiredService<IMessageTransport>();

    public MessagePipeline Pipeline => host.Services.GetRequiredService<MessagePipeline>();

    public OutboxDispatcher Dispatcher => host.Services.GetRequiredService<OutboxDispatcher>();

    public HandlerBehaviour Behaviour => host.Services.GetRequiredService<HandlerBehaviour>();

    public static async Task<TestService> StartAsync(PostgresFixture postgres, string name, Action<IServiceCollection>? configure = null)
    {
        var connectionString = await postgres.CreateServiceSchemaAsync(name);

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddNpgsqlDbContext<TestDbContext>(connectionString);
        builder.Services.AddMigrationsOnStartup<TestDbContext>();
        builder.Services.AddSingleton<IMessageTransport, InMemoryTransport>();
        builder.Services.AddSingleton<HandlerBehaviour>();
        builder.Services.AddBankMessaging(options =>
        {
            options.ServiceName = name;
            options.PollingInterval = TimeSpan.FromMilliseconds(50);
        });
        configure?.Invoke(builder.Services);

        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return new TestService(host, connectionString);
    }

    /// <summary>Counts rows with plain SQL, outside EF, so the test sees what is really committed.</summary>
    public Task<long> CountAsync(string table, string where = "true") =>
        ScalarAsync<long>($"SELECT count(*) FROM {table} WHERE {where}");

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    public async ValueTask DisposeAsync()
    {
        await host.StopAsync(CancellationToken.None);
        if (host is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            host.Dispose();
        }
    }
}

public static class TestEvents
{
    public static TestEvent New(string key, int sequence = 1, decimal amount = 100.50m) =>
        new(Guid.NewGuid(), new DateOnly(2026, 10, 30), "tester", key, sequence, new Money(amount, "SEK"));

    /// <summary>The event as the transport would deliver it, as if a sender's dispatcher had sent it.</summary>
    public static TransportMessage AsMessage(TestEvent @event, string schemaUri = TestEvent.SchemaUri) =>
        new(@event.EventId, schemaUri, @event.Key, JsonSerializer.Serialize(@event, ContractJson.Options),
            @event.PerformedBy, @event.OccurredOn);
}
