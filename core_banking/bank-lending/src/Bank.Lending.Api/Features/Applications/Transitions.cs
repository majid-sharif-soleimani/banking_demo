using Bank.Common;
using Bank.Contracts.Events;
using Bank.Lending.Api.Data;
using Bank.Lending.Api.Logic;
using Bank.Messaging;

namespace Bank.Lending.Api.Features.Applications;

/// <summary>
/// The one way an application's status changes: ask <see cref="ApplicationStateMachine"/>, record the history row and
/// publish LoanApplicationStatusChanged, all in the caller's unit of work (ADR 0004).
/// </summary>
public static class Transitions
{
    public static async Task<Result<ApplicationStatus>> ApplyAsync(
        LoanApplication application, ApplicationAction action, string? reason, string actor, DateOnly businessDate, DateTimeOffset now,
        IEventPublisher publisher, CancellationToken ct)
    {
        var result = ApplicationStateMachine.Apply(application.Status, action);
        if (!result.IsSuccess)
        {
            return result;
        }

        var from = application.Status;
        application.Status = result.Value;
        application.PerformedBy = actor;
        application.History.Add(new ApplicationStatusChange
        {
            Id = Guid.CreateVersion7(), ApplicationId = application.Id, FromStatus = from, ToStatus = result.Value, Reason = reason,
            BusinessDate = businessDate, CreatedAt = now, PerformedBy = actor,
        });
        await publisher.PublishAsync(
            new LoanApplicationStatusChanged(Guid.NewGuid(), businessDate, actor, application.Id, application.CustomerId, from.ToString(),
                result.Value.ToString(), reason),
            application.Id.ToString(), ct);
        return result;
    }

    public static IResult Conflict(Error error) => Problems.Of(StatusCodes.Status409Conflict, error.Code, error.Message);
}
