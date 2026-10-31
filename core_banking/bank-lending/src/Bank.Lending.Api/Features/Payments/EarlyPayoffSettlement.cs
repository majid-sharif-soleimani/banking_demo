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

        var rows = loan.Schedule.OrderBy(i => i.Number).ToList();
        var quote = EarlyPayoff.Quote([.. rows.Select(r => r.ToLogic(loan.Currency))], loan.Terms, today);
        var principal = quote.OutstandingPrincipal;
        var penalty = quote.UnpaidPenalty;
        var interest = e.Amount - principal - penalty;

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
            new LoanPaidOffEarly(Guid.NewGuid(), today, e.PerformedBy, loan.Id, loan.CustomerId, e.Amount, quote.WaivedInterest), key, ct);
    }
}
