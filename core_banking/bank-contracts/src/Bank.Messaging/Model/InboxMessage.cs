namespace Bank.Messaging.Model;

/// <summary>"This handler has applied this message": one row per (message id, handler name), ADR 0007.</summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; init; }

    public required string HandlerName { get; init; }

    public required string SchemaUri { get; init; }

    public required string PerformedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>PostgreSQL's xmin, like every table's Version. Inbox rows are only inserted, so it is never checked.</summary>
    public uint Version { get; set; }
}
