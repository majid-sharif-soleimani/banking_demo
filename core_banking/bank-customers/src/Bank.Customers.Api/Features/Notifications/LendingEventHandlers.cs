using Bank.Common;
using Bank.Contracts.Events;
using Bank.Customers.Api.Data;
using Bank.Customers.Api.Logic;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Bank.Customers.Api.Features.Notifications;

/// <summary>InformationRequested → an open information request and an email listing what is needed. Class name = inbox key.</summary>
public sealed class InformationRequestedHandler(CustomersDbContext db, IEmailSender email, TimeProvider time) : IEventHandler<InformationRequested>
{
    public async Task HandleAsync(InformationRequested e, CancellationToken ct)
    {
        if (!await db.InformationRequests.AnyAsync(r => r.Id == e.InformationRequestId, ct))
        {
            db.InformationRequests.Add(new InformationRequest
            {
                Id = e.InformationRequestId, CustomerId = e.CustomerId, ApplicationId = e.ApplicationId, RequestedItems = e.RequestedItems,
                Status = InformationRequestStatus.Open, RequestedOn = e.OccurredOn, CreatedAt = time.GetUtcNow(), PerformedBy = e.PerformedBy,
            });
        }

        await Notifier.NotifyAsync(db, email, time, e.CustomerId, e.EventId, EmailTemplates.InformationRequestedTemplate,
            c => EmailTemplates.InformationRequested(c.Email, c.Name, e.RequestedItems), new { e.ApplicationId, e.InformationRequestId }, e.PerformedBy, ct);
    }
}

/// <summary>LoanApplicationSubmitted → application_ref (who owns it) and a note in the interaction history (design plan section 6).</summary>
public sealed class LoanApplicationSubmittedHandler(CustomersDbContext db, TimeProvider time) : IEventHandler<LoanApplicationSubmitted>
{
    public async Task HandleAsync(LoanApplicationSubmitted e, CancellationToken ct)
    {
        if (!await db.Applications.AnyAsync(a => a.ApplicationId == e.ApplicationId, ct))
        {
            db.Applications.Add(new ApplicationRef { ApplicationId = e.ApplicationId, CustomerId = e.CustomerId, CreatedAt = time.GetUtcNow(), PerformedBy = e.PerformedBy });
        }

        await History.RecordAsync(db, time, e.CustomerId, e.ApplicationId, e.EventId, e.OccurredOn, e.PerformedBy,
            $"Applied for {e.ProductCode}: {e.RequestedAmount} over {e.TermMonths} months.", ct);
    }
}

/// <summary>LoanApplicationStatusChanged → a note in the interaction history.</summary>
public sealed class LoanApplicationStatusChangedHandler(CustomersDbContext db, TimeProvider time) : IEventHandler<LoanApplicationStatusChanged>
{
    public Task HandleAsync(LoanApplicationStatusChanged e, CancellationToken ct) =>
        History.RecordAsync(db, time, e.CustomerId, e.ApplicationId, e.EventId, e.OccurredOn, e.PerformedBy,
            $"Application status {e.FromStatus} → {e.ToStatus}" + (e.Reason is null ? "." : $": {e.Reason}"), ct);
}

/// <summary>PaymentFailed → email (installment or payoff).</summary>
public sealed class PaymentFailedHandler(CustomersDbContext db, IEmailSender email, TimeProvider time) : IEventHandler<PaymentFailed>
{
    public Task HandleAsync(PaymentFailed e, CancellationToken ct) =>
        Notifier.NotifyAsync(db, email, time, e.CustomerId, e.EventId, EmailTemplates.PaymentFailedTemplate,
            c => EmailTemplates.PaymentFailed(c.Email, c.Name, e.Amount, e.Purpose), new { e.LoanId, e.Purpose, e.Amount }, e.PerformedBy, ct);
}

/// <summary>
/// LoanClassificationChanged → a warning email when the loan gets worse (Overdue, Default). A return to Current sends
/// nothing: the design plan names this a warning (section 6).
/// </summary>
public sealed class LoanClassificationChangedHandler(CustomersDbContext db, IEmailSender email, TimeProvider time) : IEventHandler<LoanClassificationChanged>
{
    public Task HandleAsync(LoanClassificationChanged e, CancellationToken ct) =>
        e.ToClassification == "Current"
            ? Task.CompletedTask
            : Notifier.NotifyAsync(db, email, time, e.CustomerId, e.EventId, EmailTemplates.ClassificationWarningTemplate,
                c => EmailTemplates.ClassificationWarning(c.Email, c.Name, e.ToClassification, e.DaysPastDue),
                new { e.LoanId, e.FromClassification, e.ToClassification, e.DaysPastDue }, e.PerformedBy, ct);
}

/// <summary>LoanPaidOffEarly → confirmation email.</summary>
public sealed class LoanPaidOffEarlyHandler(CustomersDbContext db, IEmailSender email, TimeProvider time) : IEventHandler<LoanPaidOffEarly>
{
    public Task HandleAsync(LoanPaidOffEarly e, CancellationToken ct) =>
        Notifier.NotifyAsync(db, email, time, e.CustomerId, e.EventId, EmailTemplates.PaidOffEarlyTemplate,
            c => EmailTemplates.PaidOffEarly(c.Email, c.Name, e.Amount, e.WaivedInterest), new { e.LoanId, e.Amount, e.WaivedInterest }, e.PerformedBy, ct);
}

internal static class History
{
    public static async Task RecordAsync(
        CustomersDbContext db, TimeProvider time, Guid customerId, Guid applicationId, Guid sourceEventId, DateOnly date, string actor, string text,
        CancellationToken ct)
    {
        if (await db.Interactions.AnyAsync(i => i.SourceEventId == sourceEventId, ct))
        {
            return;
        }

        db.Interactions.Add(new Interaction
        {
            Id = Guid.CreateVersion7(), CustomerId = customerId, ApplicationId = applicationId, Kind = InteractionKind.Note, Content = text,
            ReceivedOn = date, SourceEventId = sourceEventId, CreatedAt = time.GetUtcNow(), PerformedBy = PerformedBy.From(actor),
        });
    }
}
