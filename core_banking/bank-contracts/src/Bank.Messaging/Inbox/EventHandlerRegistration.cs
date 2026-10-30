using Bank.Contracts.Events;
using Bank.Contracts.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Messaging.Inbox;

/// <summary>One IEventHandler&lt;T&gt; registered with AddEventHandler; the pipeline routes messages to it by schema.</summary>
public sealed class EventHandlerRegistration
{
    private readonly Func<IServiceProvider, object, CancellationToken, Task> invoke;

    private EventHandlerRegistration(Type eventType, string handlerName, Func<IServiceProvider, object, CancellationToken, Task> invoke)
    {
        EventType = eventType;
        Schema = EventSchemaUri.Of(eventType);
        HandlerName = handlerName;
        this.invoke = invoke;
    }

    public Type EventType { get; }

    public SchemaKey Schema { get; }

    /// <summary>The handler's class name: part of the inbox key, so renaming a handler makes it see old messages as new.</summary>
    public string HandlerName { get; }

    public static EventHandlerRegistration For<TEvent, THandler>()
        where TEvent : IIntegrationEvent
        where THandler : class, IEventHandler<TEvent> =>
        new(typeof(TEvent), typeof(THandler).Name,
            (services, @event, ct) => services.GetRequiredService<THandler>().HandleAsync((TEvent)@event, ct));

    internal Task InvokeAsync(IServiceProvider services, object @event, CancellationToken ct) => invoke(services, @event, ct);
}
