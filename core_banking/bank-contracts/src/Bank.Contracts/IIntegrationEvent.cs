using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Bank.Contracts;

/// <summary>
/// An event that crosses a service boundary. Every implementing record also declares
/// <c>public const string SchemaUri</c>, the version of its contract (checked by the contract tests).
/// The attributes here also describe these fields in every generated JSON Schema.
/// </summary>
public interface IIntegrationEvent
{
    [Description("Unique id of this event; the consumer inbox key.")]
    Guid EventId { get; }

    [Description("Business date on which the event happened.")]
    DateOnly OccurredOn { get; }

    [Description("Actor id that caused the event, 'system' by default.")]
    [MinLength(1)]
    string PerformedBy { get; }
}
