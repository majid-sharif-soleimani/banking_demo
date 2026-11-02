using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Bank.Contracts.Events;

[Description("An in-person appointment was booked for an application; Lending changes its status.")]
public sealed record AppointmentScheduled(
    Guid EventId,
    DateOnly OccurredOn,
    string PerformedBy,
    [property: Description("Id of the appointment.")] Guid AppointmentId,
    [property: Description("Id of the application.")] Guid ApplicationId,
    [property: Description("Employee, for example E001.")][property: RegularExpression("^E[0-9]{3}$")] string EmployeeId,
    [property: Description("Start of the 60-minute slot.")] DateTimeOffset StartsAt)
    : IIntegrationEvent
{
    public const string SchemaUri = "iglu:se.bank/appointment_scheduled/jsonschema/1-0-0";
}
