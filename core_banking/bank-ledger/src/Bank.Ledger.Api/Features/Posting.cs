using Bank.Contracts.Events;
using Bank.Ledger.Api.Data;
using Bank.Ledger.Api.Logic;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Bank.Ledger.Api.Features;

/// <summary>
/// Stores the drafts a posting rule produced for one event. Idempotent: an event whose message id already has entries
/// posts nothing (and the unique (source message, currency) index backs that up). An unbalanced draft is a bug: it throws.
/// </summary>
public static class Posting
{
    public static async Task PostAsync(
        LedgerDbContext db, TimeProvider time, IIntegrationEvent e, string sourceEvent, IReadOnlyList<JournalDraft> drafts, CancellationToken ct)
    {
        if (drafts.Count == 0 || await db.Entries.AnyAsync(x => x.SourceMessageId == e.EventId, ct))
        {
            return;
        }

        var now = time.GetUtcNow();
        foreach (var draft in drafts)
        {
            if (!draft.IsBalanced)
            {
                throw new InvalidOperationException($"Unbalanced journal draft for {sourceEvent} {e.EventId} in {draft.Currency}.");
            }

            if (!await db.Accounts.AnyAsync(a => a.Currency == draft.Currency, ct))
            {
                throw new InvalidOperationException($"No chart of accounts for {draft.Currency}; CurrencyActivated has not arrived.");
            }

            var entry = new JournalEntry
            {
                Id = Guid.CreateVersion7(), BusinessDate = e.OccurredOn, Currency = draft.Currency, Description = draft.Description,
                SourceEvent = sourceEvent, SourceMessageId = e.EventId, CreatedAt = now, PerformedBy = e.PerformedBy,
            };
            entry.Lines.AddRange(draft.Lines.Select(l => new JournalLineRow
            {
                Id = Guid.CreateVersion7(), EntryId = entry.Id, GlCode = l.Code, Currency = draft.Currency, Debit = l.Debit, Credit = l.Credit,
                CreatedAt = now, PerformedBy = e.PerformedBy,
            }));
            db.Entries.Add(entry);
        }
    }
}

/// <summary>The chart of accounts for a newly activated currency. Idempotent. Class names below are inbox keys.</summary>
public sealed class CurrencyActivatedHandler(LedgerDbContext db, TimeProvider time) : IEventHandler<CurrencyActivated>
{
    public async Task HandleAsync(CurrencyActivated e, CancellationToken ct)
    {
        if (await db.Accounts.AnyAsync(a => a.Currency == e.Code, ct))
        {
            return;
        }

        db.Accounts.AddRange(GlAccounts.Chart.Select(a => new GlAccount
        {
            Code = a.Code, Currency = e.Code, Name = a.Name, Type = a.Type, CreatedAt = time.GetUtcNow(), PerformedBy = e.PerformedBy,
        }));
    }
}

public sealed class MoneyDepositedHandler(LedgerDbContext db, TimeProvider time) : IEventHandler<MoneyDeposited>
{
    public Task HandleAsync(MoneyDeposited e, CancellationToken ct) =>
        Posting.PostAsync(db, time, e, nameof(MoneyDeposited), PostingRules.Deposit(e.Amount), ct);
}

public sealed class MoneyWithdrawnHandler(LedgerDbContext db, TimeProvider time) : IEventHandler<MoneyWithdrawn>
{
    public Task HandleAsync(MoneyWithdrawn e, CancellationToken ct) =>
        Posting.PostAsync(db, time, e, nameof(MoneyWithdrawn), PostingRules.Withdrawal(e.Amount), ct);
}

public sealed class MoneyTransferredHandler(LedgerDbContext db, TimeProvider time) : IEventHandler<MoneyTransferred>
{
    public Task HandleAsync(MoneyTransferred e, CancellationToken ct) =>
        Posting.PostAsync(db, time, e, nameof(MoneyTransferred), PostingRules.Transfer(e.Amount, e.ConvertedAmount), ct);
}

public sealed class LoanDisbursedHandler(LedgerDbContext db, TimeProvider time) : IEventHandler<LoanDisbursed>
{
    public Task HandleAsync(LoanDisbursed e, CancellationToken ct) =>
        Posting.PostAsync(db, time, e, nameof(LoanDisbursed), PostingRules.Disbursement(e.Principal), ct);
}

public sealed class LoanPaymentAllocatedHandler(LedgerDbContext db, TimeProvider time) : IEventHandler<LoanPaymentAllocated>
{
    public Task HandleAsync(LoanPaymentAllocated e, CancellationToken ct) =>
        Posting.PostAsync(db, time, e, nameof(LoanPaymentAllocated), PostingRules.PaymentAllocated(e.Source, e.Principal, e.Interest, e.Penalty), ct);
}
