using Bank.Common;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Lending.Api.Data;
using Bank.Lending.Api.Logic;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Bank.Lending.Api.Features.Payments;

/// <summary>
/// The answers to Lending's collection requests (ADR 0005), and card payments:
/// <list type="bullet">
/// <item>PaymentCollected (Installment) / CardPaymentCaptured → Allocation.Apply, LoanPaymentAllocated, PaidOff when every
/// item is paid;</item>
/// <item>PaymentCollected (Payoff) → every item settled, future interest waived, PaidOffEarly, LoanPaidOffEarly;</item>
/// <item>PaymentFailed → the collection closes; the item stays unpaid and is asked for again at the next end of day.</item>
/// </list>
/// Each payment is allocated once: loan_payment.source_id (the collection or card payment id) is unique.
/// Class names are inbox keys.
/// </summary>
public sealed class PaymentCollectedHandler(LendingDbContext db, IEventPublisher publisher, IBusinessClock clock, TimeProvider time)
    : IEventHandler<PaymentCollected>
{
    public async Task HandleAsync(PaymentCollected e, CancellationToken ct)
    {
        if (await db.Payments.AnyAsync(p => p.SourceId == e.CollectionId, ct))
        {
            return;
        }

        var loan = await LoanPayments.LoadAsync(db, e.LoanId, ct);
        if (loan.Status != LoanStatus.Active)
        {
            // Money was taken for a loan that is already settled: never book it silently; the dead-letter topic shows it.
            throw new InvalidOperationException($"PaymentCollected {e.CollectionId} for loan {loan.Id}, which is {loan.Status}.");
        }

        if (e.Purpose == "Payoff")
        {
            await EarlyPayoffSettlement.SettleAsync(db, publisher, loan, e, clock.Today, time.GetUtcNow(), ct);
            return;
        }

        // Allocate as of the collected item's due date when Lending's clock has not reached it yet (end of day D, before D+1 opens).
        var asOf = clock.Today;
        foreach (var row in loan.Schedule.Where(r => r.PendingCollectionId == e.CollectionId))
        {
            row.PendingCollectionId = null;
            asOf = row.DueDate > asOf ? row.DueDate : asOf;
        }

        await LoanPayments.AllocateAsync(db, publisher, loan, e.CollectionId, PaymentSource.Account, e.Amount, null, clock.Today, asOf, time.GetUtcNow(), e.PerformedBy, ct);
    }
}

public sealed class CardPaymentCapturedHandler(LendingDbContext db, IEventPublisher publisher, IBusinessClock clock, TimeProvider time)
    : IEventHandler<CardPaymentCaptured>
{
    public async Task HandleAsync(CardPaymentCaptured e, CancellationToken ct)
    {
        if (await db.Payments.AnyAsync(p => p.SourceId == e.PaymentId, ct))
        {
            return;
        }

        var loan = await LoanPayments.LoadAsync(db, e.LoanId, ct);
        // The card network already took the money: a closed loan allocates nothing, and the whole amount is recorded as
        // unallocated (owed back to the customer) instead of disappearing (review of 2026-10-31, ledger ADR 0002).
        await LoanPayments.AllocateAsync(db, publisher, loan, e.PaymentId, PaymentSource.Card, e.Amount, e.ScheduleItemNumber, clock.Today, clock.Today, time.GetUtcNow(), e.PerformedBy, ct);
    }
}

public sealed class PaymentFailedHandler(LendingDbContext db) : IEventHandler<PaymentFailed>
{
    public async Task HandleAsync(PaymentFailed e, CancellationToken ct)
    {
        var loan = await LoanPayments.LoadAsync(db, e.LoanId, ct);
        if (loan.PendingPayoffCollectionId == e.CollectionId)
        {
            // The payoff failed: the loan stays Active (stage 5 file).
            loan.PendingPayoffCollectionId = null;
        }

        foreach (var row in loan.Schedule.Where(r => r.PendingCollectionId == e.CollectionId))
        {
            row.PendingCollectionId = null;
        }
    }
}

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment {SourceId} for loan {LoanId} left {Amount} unallocated; it needs a refund.")]
    public static partial void Unallocated(ILogger logger, Guid loanId, Guid sourceId, string amount);
}

internal static class LoanPayments
{
    public static async Task<Loan> LoadAsync(LendingDbContext db, Guid loanId, CancellationToken ct) =>
        await db.Loans.Include(l => l.Schedule).SingleOrDefaultAsync(l => l.Id == loanId, ct)
        ?? throw new InvalidOperationException($"Payment for unknown loan {loanId}.");

    /// <param name="itemNumber">A card payment for one chosen installment; null allocates to all items due (oldest first).</param>
    /// <param name="asOf">Items due on or before it are paid penalty, interest, principal; money left prepays later items,
    /// pro rata between principal and interest for a partly covered one (ADR 0003, point 5).</param>
    public static async Task AllocateAsync(
        LendingDbContext db, IEventPublisher publisher, Loan loan, Guid sourceId, PaymentSource source, Money amount, int? itemNumber,
        DateOnly today, DateOnly asOf, DateTimeOffset now, string actor, CancellationToken ct)
    {
        var rows = loan.Schedule.OrderBy(i => i.Number).ToList();
        var eligible = loan.Status == LoanStatus.Active ? rows.Where(r => itemNumber is null || r.Number == itemNumber).ToList() : [];
        var result = Allocation.Apply([.. eligible.Select(r => r.ToLogic(loan.Currency))], loan.Terms, amount, asOf).Value;
        foreach (var item in result.Schedule)
        {
            var row = rows.Single(r => r.Number == item.Number);
            row.Apply(item);
            row.PerformedBy = actor;
        }

        Money Sum(Func<ItemAllocation, Money> part) => result.Allocations.Select(part).Aggregate(Money.Zero(loan.Currency), (a, b) => a + b);
        var principal = Sum(a => a.Principal);
        var interest = Sum(a => a.Interest);
        var penalty = Sum(a => a.Penalty);
        db.Payments.Add(new LoanPayment
        {
            Id = Guid.CreateVersion7(), LoanId = loan.Id, SourceId = sourceId, Source = source, Amount = amount.Amount, Principal = principal.Amount,
            Interest = interest.Amount, Penalty = penalty.Amount, Unallocated = result.Unallocated.Amount, BusinessDate = today, CreatedAt = now, PerformedBy = actor,
        });
        await publisher.PublishAsync(
            new LoanPaymentAllocated(Guid.NewGuid(), today, actor, loan.Id, sourceId, source.ToString(), principal, interest, penalty,
                result.Unallocated.Amount == 0m ? null : result.Unallocated), loan.Id.ToString(), ct);

        if (result.Unallocated.Amount > 0m)
        {
            // Over-collection: more than every eligible item still owes (for example a card payment on an item that was
            // collected meanwhile, or more than the whole loan). Kept on the payment row and logged, so someone refunds it.
            // There is no automatic refund in phase 1.
            Log.Unallocated(db.GetService<ILoggerFactory>().CreateLogger("Bank.Lending.Payments"), loan.Id, sourceId, result.Unallocated.ToString());
        }

        if (loan.Status == LoanStatus.Active && rows.All(r => r.Status == ScheduleItemStatus.Paid))
        {
            loan.Status = LoanStatus.PaidOff;
            loan.ClosedOn = today;
            loan.Classification = LoanClassification.Current;
            loan.PerformedBy = actor;
        }
    }
}
