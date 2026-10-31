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

            // CurrencyActivated (operations topic) and a posting (accounts or lending topic) can arrive in either order:
            // the chart is created on first use, so a posting is never dead-lettered for that (review of 2026-10-31).
            await Chart.EnsureAsync(db, draft.Currency, e.PerformedBy, now, ct);

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

public static class Chart
{
    /// <summary>Adds the accounts of the chart that are missing for a currency (all of them the first time).</summary>
    public static async Task EnsureAsync(LedgerDbContext db, string currency, string actor, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.Accounts.Where(a => a.Currency == currency).Select(a => a.Code).ToListAsync(ct);
        existing.AddRange(db.Accounts.Local.Where(a => a.Currency == currency).Select(a => a.Code));
        db.Accounts.AddRange(GlAccounts.Chart.Where(a => !existing.Contains(a.Code)).Select(a => new GlAccount
        {
            Code = a.Code, Currency = currency, Name = a.Name, Type = a.Type, CreatedAt = now, PerformedBy = actor,
        }));
    }
}

/// <summary>The chart of accounts for a newly activated currency. Idempotent. Class names below are inbox keys.</summary>
public sealed class CurrencyActivatedHandler(LedgerDbContext db, TimeProvider time) : IEventHandler<CurrencyActivated>
{
    public async Task HandleAsync(CurrencyActivated e, CancellationToken ct)
    {
        await Chart.EnsureAsync(db, e.Code, e.PerformedBy, time.GetUtcNow(), ct);
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
        Posting.PostAsync(db, time, e, nameof(LoanPaymentAllocated), PostingRules.PaymentAllocated(e.Source, e.Principal, e.Interest, e.Penalty, e.Unallocated), ct);
}
