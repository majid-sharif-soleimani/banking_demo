using Bank.Accounts.Api.Data;
using Bank.Accounts.Api.Features.Movements;
using Bank.Common;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Bank.Accounts.Api.Features.Loans;

/// <summary>
/// Collects what Lending asks for, in full or not at all (no partial debits, stage 5 file): InstallmentDue answers
/// PaymentCollected or PaymentFailed with purpose Installment, LoanPayoffRequested the same with purpose Payoff. Each
/// collection id is answered once (loan_collection primary key, bank-lending ADR 0005), so a duplicate request, under the
/// same or a new message id, never debits twice. Booked on Accounts' business date. Class names are inbox keys.
/// </summary>
public sealed class InstallmentDueHandler(AccountsDbContext db, IEventPublisher publisher, IBusinessClock clock, TimeProvider time)
    : IEventHandler<InstallmentDue>
{
    public Task HandleAsync(InstallmentDue e, CancellationToken ct) =>
        LoanCollections.CollectAsync(db, publisher, clock, time, e.LoanId, e.CollectionId, e.ScheduleItemNumber, "Installment", e.AccountId, e.Amount, e.PerformedBy, ct);
}

public sealed class LoanPayoffRequestedHandler(AccountsDbContext db, IEventPublisher publisher, IBusinessClock clock, TimeProvider time)
    : IEventHandler<LoanPayoffRequested>
{
    public Task HandleAsync(LoanPayoffRequested e, CancellationToken ct) =>
        LoanCollections.CollectAsync(db, publisher, clock, time, e.LoanId, e.CollectionId, null, "Payoff", e.AccountId, e.Amount, e.PerformedBy, ct);
}

internal static class LoanCollections
{
    public const string InsufficientFunds = "insufficient_funds";

    public static async Task CollectAsync(
        AccountsDbContext db, IEventPublisher publisher, IBusinessClock clock, TimeProvider time, Guid loanId, Guid collectionId, int? itemNumber,
        string purpose, Guid accountId, Money amount, string actor, CancellationToken ct)
    {
        if (await db.LoanCollections.AnyAsync(c => c.CollectionId == collectionId, ct))
        {
            return;
        }

        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId, ct)
            ?? throw new InvalidOperationException($"Collection {collectionId} for unknown account {accountId}.");
        if (account.Currency != amount.Currency)
        {
            throw new InvalidOperationException($"Collection {collectionId} is in {amount.Currency}, account {accountId} in {account.Currency}.");
        }

        var today = clock.Today;
        var collected = account.Balance >= amount.Amount;
        if (collected)
        {
            var minorUnits = await CashMovements.MinorUnitsAsync(db, account.Currency, ct);
            Postings.Post(db, account, -amount.Amount, minorUnits, EntryKind.LoanRepayment, loanId,
                purpose == "Payoff" ? "Loan payoff" : $"Loan installment {itemNumber}", today, actor, time.GetUtcNow());
            await publisher.PublishAsync(new PaymentCollected(Guid.NewGuid(), today, actor, loanId, collectionId, itemNumber, purpose, amount), loanId.ToString(), ct);
        }
        else
        {
            await publisher.PublishAsync(
                new PaymentFailed(Guid.NewGuid(), today, actor, loanId, collectionId, account.CustomerId, itemNumber, purpose, amount, InsufficientFunds),
                loanId.ToString(), ct);
        }

        db.LoanCollections.Add(new LoanCollection
        {
            CollectionId = collectionId, LoanId = loanId, ScheduleItemNumber = itemNumber, Purpose = purpose, AccountId = accountId, Amount = amount.Amount,
            Currency = amount.Currency, Status = collected ? "Collected" : "Failed", Reason = collected ? null : InsufficientFunds,
            BusinessDate = today, CreatedAt = time.GetUtcNow(), PerformedBy = actor,
        });
    }
}
