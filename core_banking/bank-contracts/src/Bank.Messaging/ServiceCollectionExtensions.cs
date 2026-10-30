using System.Reflection;
using Bank.Contracts.Events;
using Bank.Messaging.Data;
using Bank.Messaging.Inbox;
using Bank.Messaging.Migrations;
using Bank.Messaging.Outbox;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Bank.Messaging;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers DbSession, IEventPublisher (outbox), the outbox dispatcher and the consumer pipeline.
    /// The service must also register an IDbConnectionFactory for its own schema (AddNpgsqlConnectionFactory)
    /// and an IMessageTransport (AddKafkaTransport from slice 1.3, or the in-memory one in tests).
    /// </summary>
    public static IServiceCollection AddBankMessaging(this IServiceCollection services, Action<BankMessagingOptions> configure)
    {
        // Snake-case columns map to PascalCase properties in every Dapper query of the service.
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        services.AddOptions<BankMessagingOptions>().Configure(configure)
            .Validate(o => !string.IsNullOrWhiteSpace(o.ServiceName), "BankMessaging: ServiceName is required.")
            .Validate(o => o.BatchSize > 0 && o.PollingInterval > TimeSpan.Zero, "BankMessaging: BatchSize and PollingInterval must be positive.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<DbSession>();
        services.TryAddScoped<IEventPublisher, OutboxEventPublisher>();
        services.TryAddSingleton<OutboxDispatcher>();
        services.TryAddSingleton<MessagePipeline>();
        services.AddHostedService<OutboxDispatcherService>();
        return services;
    }

    /// <summary>
    /// Registers the PostgreSQL <see cref="IDbConnectionFactory"/> for the service's own role and schema.
    /// The container owns it and disposes its connection pool when the host stops.
    /// </summary>
    public static IServiceCollection AddNpgsqlConnectionFactory(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.TryAddSingleton<IDbConnectionFactory>(_ => new NpgsqlConnectionFactory(connectionString));
        return services;
    }

    /// <summary>Registers a handler; the pipeline gives it every message of TEvent's schema (and compatible ADDITIONs).</summary>
    public static IServiceCollection AddEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IIntegrationEvent
        where THandler : class, IEventHandler<TEvent>
    {
        services.TryAddScoped<THandler>();
        services.AddSingleton(EventHandlerRegistration.For<TEvent, THandler>());
        return services;
    }

    /// <summary>
    /// Applies the messaging tables, then the service's own scripts (embedded under migrations/ in
    /// <paramref name="assembly"/>), before any hosted service starts.
    /// </summary>
    public static IServiceCollection AddSqlMigrations(this IServiceCollection services, Assembly assembly, string serviceName)
    {
        IReadOnlyList<MigrationScript> scripts = [.. MigrationScripts.Messaging, .. MigrationScripts.FromAssembly(assembly, serviceName)];

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<SqlMigrationRunner>();
        services.AddSingleton<IHostedService>(sp => new MigrationStartupService(sp.GetRequiredService<SqlMigrationRunner>(), scripts));
        return services;
    }
}
