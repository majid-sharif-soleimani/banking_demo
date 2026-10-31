using System.Text.Json;
using Bank.Common;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Customers.Api.Data;
using Bank.Customers.Api.Logic;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Bank.Customers.Api.Features.Notifications;

/// <summary>
/// Emails the customer when a standing order failed (design plan section 6) and records the notification. At most one
/// notification per event: the inbox stops a redelivery, and notification.source_event_id is unique. The email is sent
/// before the commit; a crash in between can send it twice (at least once), never zero times.
/// The class name is part of the inbox key: do not rename it.
/// </summary>
public sealed class StandingOrderFailedHandler(CustomersDbContext db, IEmailSender email, TimeProvider time) : IEventHandler<StandingOrderFailed>
{
    public async Task HandleAsync(StandingOrderFailed @event, CancellationToken ct)
    {
        if (await db.Notifications.AnyAsync(n => n.SourceEventId == @event.EventId, ct))
        {
            return;
        }

        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == @event.CustomerId, ct)
            ?? throw new InvalidOperationException($"StandingOrderFailed for unknown customer {@event.CustomerId}.");

        await email.SendAsync(EmailTemplates.StandingOrderFailed(customer.Email, customer.Name, @event.Amount, @event.RunDate, @event.Reason), ct);

        var now = time.GetUtcNow();
        db.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customer.Id,
            Channel = "Email",
            Template = EmailTemplates.StandingOrderFailedTemplate,
            Payload = JsonSerializer.Serialize(new { @event.StandingOrderId, @event.Amount, @event.RunDate, @event.Reason }, ContractJson.Options),
            Status = NotificationStatus.Sent,
            SentAt = now,
            SourceEventId = @event.EventId,
            CreatedAt = now,
            PerformedBy = @event.PerformedBy,
        });
    }
}
