using Bank.Common;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Lending.Api.Data;
using Bank.Lending.Api.Logic;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;

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

internal static class LoanPayments
{
    public static async Task<Loan> LoadAsync(LendingDbContext db, Guid loanId, CancellationToken ct) =>
        await db.Loans.Include(l => l.Schedule).SingleOrDefaultAsync(l => l.Id == loanId, ct)
        ?? throw new InvalidOperationException($"Payment for unknown loan {loanId}.");

    /// <param name="itemNumber">A card payment for one chosen installment; null allocates to all items due (oldest first).</param>
    /// <param name="asOf">Items due on or before it can be paid; later ones are never prepaid (ADR 0003, point 5).</param>
    public static async Task AllocateAsync(
        LendingDbContext db, IEventPublisher publisher, Loan loan, Guid sourceId, PaymentSource source, Money amount, int? itemNumber,
        DateOnly today, DateOnly asOf, DateTimeOffset now, string actor, CancellationToken ct)
    {
        var rows = loan.Schedule.OrderBy(i => i.Number).ToList();
        var eligible = rows.Where(r => itemNumber is null || r.Number == itemNumber).ToList();
        var result = Allocation.Apply([.. eligible.Select(r => r.ToLogic(loan.Currency))], amount, asOf).Value;
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
            new LoanPaymentAllocated(Guid.NewGuid(), today, actor, loan.Id, sourceId, source.ToString(), principal, interest, penalty), loan.Id.ToString(), ct);

        if (rows.All(r => r.Status == ScheduleItemStatus.Paid))
        {
            loan.Status = LoanStatus.PaidOff;
            loan.Classification = LoanClassification.Current;
            loan.PerformedBy = actor;
        }
    }
}
