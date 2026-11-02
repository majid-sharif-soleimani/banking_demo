using System.Text.Json;
using Bank.Common;
using Bank.Contracts;
using Bank.Customers.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Bank.Customers.Api.Features.Notifications;

/// <summary>
/// Sends one email for one event and records it: at most one notification per event (unique source_event_id), sent before
/// the handler commits, so at least once and never zero times (see StandingOrderFailedHandler).
/// </summary>
public static class Notifier
{
    public static async Task NotifyAsync(
        CustomersDbContext db, IEmailSender email, TimeProvider time, Guid customerId, Guid sourceEventId, string template,
        Func<Customer, EmailMessage> compose, object payload, string actor, CancellationToken ct)
    {
        if (await db.Notifications.AnyAsync(n => n.SourceEventId == sourceEventId, ct))
        {
            return;
        }

        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new InvalidOperationException($"Notification for unknown customer {customerId}.");
        await email.SendAsync(compose(customer), ct);
        var now = time.GetUtcNow();
        db.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(), CustomerId = customerId, Channel = "Email", Template = template,
            Payload = JsonSerializer.Serialize(payload, ContractJson.Options), Status = NotificationStatus.Sent, SentAt = now,
            SourceEventId = sourceEventId, CreatedAt = now, PerformedBy = actor,
        });
    }
}
