namespace Bank.Contracts;

/// <summary>
/// An event that crosses a service boundary. Every implementing record also declares
/// <c>public const string SchemaUri</c> pointing at its JSON Schema (checked by the contract tests).
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>Unique per event; consumers use it as the inbox key.</summary>
    Guid EventId { get; }

    /// <summary>The business date (from Operations) on which the event happened, not the wall clock.</summary>
    DateOnly OccurredOn { get; }

    /// <summary>Actor id of whoever caused the event, "system" by default.</summary>
    string PerformedBy { get; }
}
