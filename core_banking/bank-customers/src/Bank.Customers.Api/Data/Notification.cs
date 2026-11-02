namespace Bank.Customers.Api.Data;

/// <summary>
/// One message sent to a customer. <see cref="SourceEventId"/> is unique: an event produces at most one notification,
/// even if it arrives again under the same id after a crash between sending and committing.
/// </summary>
public sealed class Notification
{
    public Guid Id { get; init; }

    public Guid CustomerId { get; init; }

    public required string Channel { get; init; }

    public required string Template { get; init; }

    /// <summary>The values the template was filled with, as JSON.</summary>
    public required string Payload { get; init; }

    public NotificationStatus Status { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public Guid SourceEventId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    /// <summary>PostgreSQL's xmin, like every table's Version.</summary>
    public uint Version { get; set; }
}

public enum NotificationStatus
{
    Sent,
}
