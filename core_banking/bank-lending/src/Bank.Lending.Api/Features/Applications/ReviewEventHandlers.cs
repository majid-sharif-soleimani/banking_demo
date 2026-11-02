using Bank.Common;
using Bank.Contracts.Events;
using Bank.Lending.Api.Data;
using Bank.Lending.Api.Logic;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Bank.Lending.Api.Features.Applications;

/// <summary>
/// InformationReceived → UnderReview, AppointmentScheduled → AppointmentScheduled. If the application has meanwhile moved
/// on (for example it was cancelled), the fact is still true but no longer changes anything: it is logged and ignored
/// instead of going to the dead-letter topic (ADR 0004). Class names are inbox keys.
/// </summary>
public sealed class InformationReceivedHandler(LendingDbContext db, IEventPublisher publisher, TimeProvider time, ILogger<InformationReceivedHandler> log)
    : IEventHandler<InformationReceived>
{
    public Task HandleAsync(InformationReceived e, CancellationToken ct) =>
        ReviewEvents.MoveAsync(db, publisher, time, log, e.ApplicationId, ApplicationAction.ReceiveInformation, e.OccurredOn, e.PerformedBy, ct);
}

public sealed class AppointmentScheduledHandler(LendingDbContext db, IEventPublisher publisher, TimeProvider time, ILogger<AppointmentScheduledHandler> log)
    : IEventHandler<AppointmentScheduled>
{
    public Task HandleAsync(AppointmentScheduled e, CancellationToken ct) =>
        ReviewEvents.MoveAsync(db, publisher, time, log, e.ApplicationId, ApplicationAction.ScheduleAppointment, e.OccurredOn, e.PerformedBy, ct);
}

internal static class ReviewEvents
{
    public static async Task MoveAsync(
        LendingDbContext db, IEventPublisher publisher, TimeProvider time, ILogger log, Guid applicationId, ApplicationAction action,
        DateOnly businessDate, string actor, CancellationToken ct)
    {
        var application = await db.Applications.Include(a => a.History).SingleOrDefaultAsync(a => a.Id == applicationId, ct)
            ?? throw new InvalidOperationException($"{action} for unknown application {applicationId}.");
        var result = await Transitions.ApplyAsync(application, action, null, PerformedBy.From(actor), businessDate, time.GetUtcNow(), publisher, ct);
        if (!result.IsSuccess)
        {
            log.LogWarning("Ignored {Action} for application {ApplicationId}: {Reason}", action, applicationId, result.Error.Message);
        }
    }
}
