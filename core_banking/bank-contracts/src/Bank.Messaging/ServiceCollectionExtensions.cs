using Bank.Contracts.Events;
using Bank.Messaging.Inbox;
using Bank.Messaging.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bank.Messaging;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IEventPublisher (outbox), the outbox dispatcher and the consumer pipeline. The service must also
    /// register its DbContext as <c>DbContext</c> (AddNpgsqlDbContext in Bank.Toolkit.Data), call
    /// <c>modelBuilder.AddBankMessaging()</c> in it so its migrations create the outbox and inbox tables, and register
    /// an IMessageTransport (AddKafkaTransport from slice 1.3, or the in-memory one in tests).
    /// </summary>
    public static IServiceCollection AddBankMessaging(this IServiceCollection services, Action<BankMessagingOptions> configure)
    {
        services.AddOptions<BankMessagingOptions>().Configure(configure)
            .Validate(o => !string.IsNullOrWhiteSpace(o.ServiceName), "BankMessaging: ServiceName is required.")
            .Validate(o => o.BatchSize > 0 && o.PollingInterval > TimeSpan.Zero, "BankMessaging: BatchSize and PollingInterval must be positive.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IEventPublisher, OutboxEventPublisher>();
        services.TryAddSingleton<OutboxDispatcher>();
        services.TryAddSingleton<MessagePipeline>();
        services.AddHostedService<OutboxDispatcherService>();
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
}
