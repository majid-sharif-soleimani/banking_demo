namespace Bank.Customers.Api.Data;

/// <summary>A bank employee who can meet customers. Seeded E001–E005 (implementation-decisions).</summary>
public sealed class Employee
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

public enum InteractionKind
{
    Upload,
    EmailReply,
    Note,
}

/// <summary>
/// Something the customer sent or the bank recorded, optionally about a loan application (design plan section 2):
/// an uploaded file (kept in IFileStorage under <see cref="FileKey"/>), an email reply, or a note. Events recorded as notes
/// carry their event id in <see cref="SourceEventId"/>, unique, so each is recorded once.
/// </summary>
public sealed class Interaction
{
    public Guid Id { get; init; }

    public Guid CustomerId { get; init; }

    public Guid? ApplicationId { get; init; }

    public InteractionKind Kind { get; init; }

    public string? FileKey { get; init; }

    public string? FileName { get; init; }

    public string? ContentType { get; init; }

    public long? FileSize { get; init; }

    public string? Content { get; init; }

    public DateOnly ReceivedOn { get; init; }

    public Guid? SourceEventId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

public enum InformationRequestStatus
{
    Open,
    Completed,
}

/// <summary>What the reviewer asked the customer for (from InformationRequested). Its id is the one Lending chose.</summary>
public sealed class InformationRequest
{
    public Guid Id { get; init; }

    public Guid CustomerId { get; init; }

    public Guid ApplicationId { get; init; }

    public required string RequestedItems { get; init; }

    public InformationRequestStatus Status { get; set; }

    public DateOnly RequestedOn { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; set; }

    public uint Version { get; set; }
}

public enum AppointmentStatus
{
    Scheduled,
    Cancelled,
}

/// <summary>An in-person meeting during loan review. An employee has at most one scheduled appointment per slot.</summary>
public sealed class Appointment
{
    public Guid Id { get; init; }

    public Guid ApplicationId { get; init; }

    public Guid CustomerId { get; init; }

    public required string EmployeeId { get; init; }

    public DateTimeOffset StartsAt { get; init; }

    public AppointmentStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; set; }

    public uint Version { get; set; }
}
