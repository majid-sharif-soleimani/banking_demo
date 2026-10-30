using System.Text.Json;
using Bank.Contracts;
using Bank.Contracts.Schema;
using Bank.Messaging.Transport;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Bank.Toolkit.Data;

namespace Bank.Messaging.Inbox;

public enum ProcessOutcome
{
    /// <summary>At least one handler applied the message.</summary>
    Handled,

    /// <summary>Every handler had already applied it (a redelivery); nothing changed.</summary>
    Duplicate,

    /// <summary>No handler in this service for this event.</summary>
    NotSubscribed,
}

/// <summary>Thrown for a message in a MODEL version this service cannot read; the transport sends it to the dead-letter topic.</summary>
public sealed class UnsupportedSchemaVersionException(string schemaUri)
    : Exception($"No handler can read {schemaUri}: its MODEL version is unknown to this service.");

/// <summary>
/// The receiving side (ADR 0007). The transport calls <see cref="ProcessAsync"/> for each message and commits the
/// offset only when it returns. Each handler runs in its own scope and transaction: insert the inbox row
/// (message id, handler name), call the handler, commit. If the row already exists, the handler is skipped.
/// An exception rolls back that handler's effect, inbox row and outgoing events, and is passed to the transport to retry.
/// </summary>
public sealed class MessagePipeline
{
    private const string InsertInbox = """
        INSERT INTO inbox_message (message_id, handler_name, schema_uri, performed_by, created_at)
        VALUES (@MessageId, @HandlerName, @SchemaUri, @PerformedBy, @CreatedAt)
        ON CONFLICT (message_id, handler_name) DO NOTHING
        """;

    private readonly IServiceScopeFactory scopeFactory;
    private readonly TimeProvider timeProvider;
    private readonly ILookup<SchemaKey, EventHandlerRegistration> handlers;

    public MessagePipeline(IEnumerable<EventHandlerRegistration> registrations, IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
    {
        var all = registrations.ToList();
        var duplicate = all.GroupBy(r => r.HandlerName).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Two event handlers are named {duplicate.Key}; handler class names are inbox keys and must be unique in a service.");
        }

        handlers = all.ToLookup(r => r.Schema);
        this.scopeFactory = scopeFactory;
        this.timeProvider = timeProvider;
    }

    public async Task<ProcessOutcome> ProcessAsync(TransportMessage message, CancellationToken ct)
    {
        var route = SchemaRouting.Resolve(SchemaKey.Parse(message.SchemaUri), handlers.Select(g => g.Key));
        switch (route.Match)
        {
            case SchemaMatch.NotSubscribed:
                return ProcessOutcome.NotSubscribed;
            case SchemaMatch.UnknownModel:
                throw new UnsupportedSchemaVersionException(message.SchemaUri);
        }

        var handled = false;
        foreach (var registration in handlers[route.Known!])
        {
            handled |= await RunHandlerAsync(registration, message, ct);
        }

        return handled ? ProcessOutcome.Handled : ProcessOutcome.Duplicate;
    }

    private async Task<bool> RunHandlerAsync(EventHandlerRegistration registration, TransportMessage message, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<DbSession>();
        await session.BeginAsync(ct);

        // A concurrent delivery of the same message waits here on the primary key until the first one commits.
        var inserted = await session.Connection.ExecuteAsync(new CommandDefinition(InsertInbox, new
        {
            message.MessageId,
            registration.HandlerName,
            message.SchemaUri,
            message.PerformedBy,
            CreatedAt = timeProvider.GetUtcNow(),
        }, session.Transaction, cancellationToken: ct));

        if (inserted == 0)
        {
            await session.RollbackAsync(ct);
            return false;
        }

        var @event = JsonSerializer.Deserialize(message.Payload, registration.EventType, ContractJson.Options)
            ?? throw new JsonException($"Message {message.MessageId} has a null payload.");
        await registration.InvokeAsync(scope.ServiceProvider, @event, ct);
        await session.CommitAsync(ct);
        return true;
    }
}
