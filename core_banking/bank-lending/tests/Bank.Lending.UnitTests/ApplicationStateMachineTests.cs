using Bank.Lending.Api.Logic;
using static Bank.Lending.Api.Logic.ApplicationAction;
using static Bank.Lending.Api.Logic.ApplicationStatus;

namespace Bank.Lending.UnitTests;

public class ApplicationStateMachineTests
{
    /// <summary>The stage 5 file's diagram, row by row. Every pair not listed here must be rejected.</summary>
    public static readonly TheoryData<ApplicationStatus, ApplicationAction, ApplicationStatus> Allowed = new()
    {
        { Submitted, Review, UnderReview },
        { UnderReview, RequestInformation, InfoRequested },
        { InfoRequested, ReceiveInformation, UnderReview },
        { UnderReview, ScheduleAppointment, AppointmentScheduled },
        { AppointmentScheduled, Review, UnderReview },
        { UnderReview, Approve, Approved },
        { Approved, Disburse, Disbursed },
        { Submitted, Reject, Rejected },
        { UnderReview, Reject, Rejected },
        { InfoRequested, Reject, Rejected },
        { AppointmentScheduled, Reject, Rejected },
        { Submitted, Cancel, Cancelled },
        { UnderReview, Cancel, Cancelled },
        { InfoRequested, Cancel, Cancelled },
        { AppointmentScheduled, Cancel, Cancelled },
    };

    public static TheoryData<ApplicationStatus, ApplicationAction> Rejected_ = Build();

    private static TheoryData<ApplicationStatus, ApplicationAction> Build()
    {
        var allowed = Allowed.Select(row => (row.Data.Item1, row.Data.Item2)).ToHashSet();
        var data = new TheoryData<ApplicationStatus, ApplicationAction>();
        foreach (var status in Enum.GetValues<ApplicationStatus>())
        {
            foreach (var action in Enum.GetValues<ApplicationAction>())
            {
                if (!allowed.Contains((status, action)))
                {
                    data.Add(status, action);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Allowed))]
    public void Apply_AllowedTransition_ReturnsTheNewStatus(ApplicationStatus from, ApplicationAction action, ApplicationStatus to) =>
        ApplicationStateMachine.Apply(from, action).Value.ShouldBe(to);

    [Theory]
    [MemberData(nameof(Rejected_))]
    public void Apply_AnyOtherTransition_IsRejected(ApplicationStatus from, ApplicationAction action)
    {
        var result = ApplicationStateMachine.Apply(from, action);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("invalid_transition");
        result.Error.Message.ShouldContain(from.ToString());
    }

    [Theory]
    [InlineData(Rejected)]
    [InlineData(Cancelled)]
    [InlineData(Disbursed)]
    public void IsFinal_EndStatuses_AreFinal(ApplicationStatus status) => ApplicationStateMachine.IsFinal(status).ShouldBeTrue();

    [Fact]
    public void Rejected_CoversEveryOtherPair() =>
        // 8 statuses × 9 actions = 72 pairs; 15 allowed, so 57 rejected.
        Rejected_.Count().ShouldBe(Enum.GetValues<ApplicationStatus>().Length * Enum.GetValues<ApplicationAction>().Length - 15);
}
