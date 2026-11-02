using Bank.Common;

namespace Bank.Lending.Api.Logic;

public enum ApplicationStatus
{
    Submitted,
    UnderReview,
    InfoRequested,
    AppointmentScheduled,
    Approved,
    Rejected,
    Disbursed,
    Cancelled,
}

public enum ApplicationAction
{
    Review,
    RequestInformation,
    ReceiveInformation,
    ScheduleAppointment,
    Approve,
    Reject,
    Cancel,
    Disburse,
    /// <summary>Not a step of its own; listed so that "submit twice" is a rejected transition like any other.</summary>
    Submit,
}

/// <summary>
/// The loan application's allowed transitions (stage 5 file), as one table (ADR 0004). Endpoints and handlers ask it
/// before they change a status, so no code path can make an illegal step. The system never decides approval itself.
/// </summary>
public static class ApplicationStateMachine
{
    private static readonly ApplicationStatus[] Open =
        [ApplicationStatus.Submitted, ApplicationStatus.UnderReview, ApplicationStatus.InfoRequested, ApplicationStatus.AppointmentScheduled];

    private static readonly Dictionary<(ApplicationStatus From, ApplicationAction Action), ApplicationStatus> Transitions = new()
    {
        [(ApplicationStatus.Submitted, ApplicationAction.Review)] = ApplicationStatus.UnderReview,
        [(ApplicationStatus.UnderReview, ApplicationAction.RequestInformation)] = ApplicationStatus.InfoRequested,
        [(ApplicationStatus.InfoRequested, ApplicationAction.ReceiveInformation)] = ApplicationStatus.UnderReview,
        [(ApplicationStatus.UnderReview, ApplicationAction.ScheduleAppointment)] = ApplicationStatus.AppointmentScheduled,
        // After the appointment date passes, or on demand.
        [(ApplicationStatus.AppointmentScheduled, ApplicationAction.Review)] = ApplicationStatus.UnderReview,
        [(ApplicationStatus.UnderReview, ApplicationAction.Approve)] = ApplicationStatus.Approved,
        [(ApplicationStatus.Approved, ApplicationAction.Disburse)] = ApplicationStatus.Disbursed,
    };

    static ApplicationStateMachine()
    {
        foreach (var status in Open)
        {
            Transitions[(status, ApplicationAction.Reject)] = ApplicationStatus.Rejected;
            Transitions[(status, ApplicationAction.Cancel)] = ApplicationStatus.Cancelled;
        }
    }

    public static Result<ApplicationStatus> Apply(ApplicationStatus from, ApplicationAction action) =>
        Transitions.TryGetValue((from, action), out var to)
            ? to
            : new Error("invalid_transition", $"An application that is {from} cannot {action}.");

    public static bool IsFinal(ApplicationStatus status) =>
        status is ApplicationStatus.Rejected or ApplicationStatus.Cancelled or ApplicationStatus.Disbursed;
}
