namespace Bank.Messaging.Model;

/// <summary>One published event, waiting in the service's outbox_message table until the dispatcher has sent it.</summary>
public sealed class OutboxMessage
{
    /// <summary>The event's EventId; sent as the message-id header and the receiver's inbox key.</summary>
    public Guid Id { get; init; }

    /// <summary>Identity column; the send order.</summary>
    public long Position { get; init; }

    public required string SchemaUri { get; init; }

    public required string MessageKey { get; init; }

    /// <summary>The event JSON, stored as jsonb.</summary>
    public required string Payload { get; init; }

    public DateOnly OccurredOn { get; init; }

    public required string PerformedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? SentAt { get; set; }

    /// <summary>Failed sends, with the last error, for diagnosis.</summary>
    public int Attempts { get; set; }

    public string? LastError { get; set; }
}
