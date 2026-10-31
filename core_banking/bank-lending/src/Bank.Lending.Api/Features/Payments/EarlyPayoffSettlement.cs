using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Lending.Api.Data;
using Bank.Lending.Api.Logic;
using Bank.Messaging;

namespace Bank.Lending.Api.Features.Payments;

/// <summary>Settles an early payoff once Accounts collected it (slice 5.5): every item paid, future interest waived.</summary>
internal static class EarlyPayoffSettlement
{
    public static async Task SettleAsync(
        LendingDbContext db, IEventPublisher publisher, Loan loan, PaymentCollected e, DateOnly today, DateTimeOffset now, CancellationToken ct)
    {
        if (loan.PendingPayoffCollectionId != e.CollectionId)
        {
            throw new InvalidOperationException($"Payoff collection {e.CollectionId} is not the open payoff of loan {loan.Id}.");
        }

        // Settle exactly the quote the payoff was requested with (stored on the loan), never a fresh one: a day passing
        // between request and answer must not move money between interest and penalty (review of 2026-10-31).
        var rows = loan.Schedule.OrderBy(i => i.Number).ToList();
        var principal = new Money(loan.PayoffPrincipal!.Value, loan.Currency);
        var penalty = new Money(loan.PayoffPenalty!.Value, loan.Currency);
        var interest = new Money(loan.PayoffInterest!.Value, loan.Currency);
        var waived = new Money(loan.PayoffWaived!.Value, loan.Currency);
        if ((principal + penalty + interest).Amount != e.Amount.Amount)
        {
            throw new InvalidOperationException($"Payoff of loan {loan.Id} collected {e.Amount}, but the quote was {principal + penalty + interest}.");
        }

        // Each item is settled as paid in full; interest beyond what was collected is waived (written down to what was paid).
        foreach (var row in rows.Where(r => r.Status != ScheduleItemStatus.Paid))
        {
            row.PaidPrincipal = row.PrincipalDue;
            row.PaidPenalty = row.PenaltyDue;
            row.InterestDue = row.PaidInterest;
            row.Status = ScheduleItemStatus.Paid;
            row.PendingCollectionId = null;
            row.PerformedBy = e.PerformedBy;
        }

        // The interest the customer paid (to date + minimum) is booked on the next unpaid item so the schedule adds up.
        var first = rows.FirstOrDefault(r => r.DueDate > today) ?? rows[^1];
        first.InterestDue += interest.Amount;
        first.PaidInterest += interest.Amount;

        loan.Status = LoanStatus.PaidOffEarly;
        loan.Classification = LoanClassification.Current;
        loan.PendingPayoffCollectionId = null;
        loan.PayoffPrincipal = loan.PayoffPenalty = loan.PayoffInterest = loan.PayoffWaived = null;
        loan.PerformedBy = e.PerformedBy;
        db.Payments.Add(new LoanPayment
        {
            Id = Guid.CreateVersion7(), LoanId = loan.Id, SourceId = e.CollectionId, Source = PaymentSource.Payoff, Amount = e.Amount.Amount,
            Principal = principal.Amount, Interest = interest.Amount, Penalty = penalty.Amount, Unallocated = 0m, BusinessDate = today,
            CreatedAt = now, PerformedBy = e.PerformedBy,
        });
        var key = loan.Id.ToString();
        await publisher.PublishAsync(
            new LoanPaymentAllocated(Guid.NewGuid(), today, e.PerformedBy, loan.Id, e.CollectionId, nameof(PaymentSource.Payoff), principal, interest, penalty), key, ct);
        await publisher.PublishAsync(
            new LoanPaidOffEarly(Guid.NewGuid(), today, e.PerformedBy, loan.Id, loan.CustomerId, e.Amount, waived), key, ct);
    }
}
