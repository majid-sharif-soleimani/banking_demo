using Bank.Accounts.Api.Data;
using Bank.Accounts.Api.Features.Transfers;
using Bank.Accounts.Api.Logic;
using Bank.Common;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Bank.Accounts.Api.Features.EndOfDay;

/// <summary>
/// Accounts' end of day for DayClosed(D) (design plan ADR 15): runs every active standing order due on or before D,
/// each in its own scope and transaction, then publishes DayCompleted(D, "accounts") with the inbox row. A failed run
/// (insufficient funds, no FX rate) is recorded with StandingOrderFailed and not retried; the next run date advances
/// either way. A redelivered DayClosed finds the orders already advanced (and runs unique per date) and changes nothing.
/// The class name is an inbox key, distinct from the business-day consumer's LocalDayClosedHandler: do not rename it.
/// </summary>
public sealed class AccountsEndOfDayHandler(AccountsDbContext db, IEventPublisher publisher, IServiceScopeFactory scopes, TimeProvider time)
    : IEventHandler<DayClosed>
{
    public const string ServiceName = "accounts";

    public async Task HandleAsync(DayClosed @event, CancellationToken ct)
    {
        // The same date again under a new message id (the inbox stops only the same id): already done, DayCompleted sent.
        if (await db.EndOfDayRuns.AnyAsync(r => r.Date == @event.Date, ct))
        {
            return;
        }

        var due = await db.StandingOrders.AsNoTracking()
            .Where(o => o.Status == StandingOrderStatus.Active && o.NextRunDate <= @event.Date)
            .OrderBy(o => o.NextRunDate).ThenBy(o => o.Id)
            .Select(o => o.Id)
            .ToListAsync(ct);

        foreach (var orderId in due)
        {
            await RunWithRetryAsync(orderId, @event.Date, ct);
        }

        // Committed with the inbox row and the outbox row by the pipeline; the primary key makes the date run once.
        db.EndOfDayRuns.Add(new EndOfDayRun { Date = @event.Date, StandingOrdersRun = due.Count, CreatedAt = time.GetUtcNow(), PerformedBy = PerformedBy.System });
        await publisher.PublishAsync(
            new DayCompleted(Guid.NewGuid(), @event.Date, PerformedBy.System, @event.Date, ServiceName), @event.Date.ToString("yyyy-MM-dd"), ct);
    }

    /// <summary>One order in its own transaction; a concurrent change to one of its accounts (xmin) reruns it once.</summary>
    private async Task RunWithRetryAsync(Guid orderId, DateOnly businessDate, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var scope = scopes.CreateAsyncScope();
            try
            {
                await RunAsync(scope.ServiceProvider, orderId, businessDate, ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt == 1)
            {
            }
        }
    }

    private static async Task RunAsync(IServiceProvider services, Guid orderId, DateOnly businessDate, CancellationToken ct)
    {
        var db = services.GetRequiredService<AccountsDbContext>();
        var publisher = services.GetRequiredService<IEventPublisher>();
        var rates = services.GetRequiredService<IExchangeRateProvider>();
        var time = services.GetRequiredService<TimeProvider>();

        var order = await db.StandingOrders.SingleAsync(o => o.Id == orderId, ct);
        // Catch up on every scheduled date up to the business date (normally exactly one).
        while (order.Status == StandingOrderStatus.Active && order.NextRunDate is { } runDate && runDate <= businessDate)
        {
            if (await db.StandingOrderRuns.AnyAsync(r => r.StandingOrderId == order.Id && r.RunDate == runDate, ct))
            {
                Advance(order, runDate);
                continue;
            }

            var from = await db.Accounts.SingleAsync(a => a.Id == order.FromAccountId, ct);
            var to = await db.Accounts.SingleAsync(a => a.Id == order.ToAccountId, ct);
            var amount = new Money(order.Amount, order.Currency);
            var (transfer, failure) = await TransferMoney.ExecuteAsync(
                db, publisher, rates, time, from, to, amount, businessDate, PerformedBy.System, order.Id, "Standing order", ct);

            var reason = failure switch
            {
                TransferFailure.None => null,
                TransferFailure.InsufficientFunds => "insufficient_funds",
                TransferFailure.FxUnavailable => "fx_unavailable",
                TransferFailure.AmountTooSmall => "amount_too_small",
                _ => "same_account",
            };
            db.StandingOrderRuns.Add(new StandingOrderRun
            {
                Id = Guid.CreateVersion7(),
                StandingOrderId = order.Id,
                RunDate = runDate,
                Status = reason is null ? StandingOrderRunStatus.Succeeded : StandingOrderRunStatus.Failed,
                Reason = reason,
                TransferId = transfer?.Id,
                CreatedAt = time.GetUtcNow(),
                PerformedBy = PerformedBy.System,
            });
            if (reason is not null)
            {
                await publisher.PublishAsync(
                    new StandingOrderFailed(Guid.NewGuid(), businessDate, PerformedBy.System, order.Id, order.CustomerId, order.FromAccountId,
                        amount, runDate, reason),
                    order.Id.ToString(), ct);
            }

            Advance(order, runDate);
        }

        await db.SaveChangesAsync(ct);
    }

    private static void Advance(StandingOrder order, DateOnly runDate)
    {
        order.NextRunDate = Logic.StandingOrders.NextRunDate(order.Schedule, runDate);
        order.PerformedBy = PerformedBy.System;
        if (order.NextRunDate is null)
        {
            order.Status = StandingOrderStatus.Completed;
        }
    }
}
