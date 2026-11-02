using Bank.Accounts.Api.Data;
using Bank.Accounts.Api.Features.Movements;
using Bank.Contracts.Events;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Bank.Accounts.Api.Features.Loans;

/// <summary>
/// Credits the repayment account with a disbursed loan (design plan section 6). No event: Ledger posts the loan from
/// LoanDisbursed itself, so a MoneyDeposited here would count it twice. Idempotent: one LoanDisbursement entry per loan.
/// The class name is an inbox key.
/// </summary>
public sealed class LoanDisbursedHandler(AccountsDbContext db, TimeProvider time) : IEventHandler<LoanDisbursed>
{
    public async Task HandleAsync(LoanDisbursed e, CancellationToken ct)
    {
        if (await db.Entries.AnyAsync(x => x.Reference == e.LoanId && x.Kind == EntryKind.LoanDisbursement, ct))
        {
            return;
        }

        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == e.RepaymentAccountId, ct)
            ?? throw new InvalidOperationException($"LoanDisbursed for unknown account {e.RepaymentAccountId}.");
        if (account.Currency != e.Principal.Currency)
        {
            throw new InvalidOperationException($"Loan {e.LoanId} is in {e.Principal.Currency}, account {account.Id} in {account.Currency}.");
        }

        var minorUnits = await CashMovements.MinorUnitsAsync(db, account.Currency, ct);
        Postings.Post(db, account, e.Principal.Amount, minorUnits, EntryKind.LoanDisbursement, e.LoanId, "Loan disbursement",
            e.OccurredOn, e.PerformedBy, time.GetUtcNow());
    }
}
