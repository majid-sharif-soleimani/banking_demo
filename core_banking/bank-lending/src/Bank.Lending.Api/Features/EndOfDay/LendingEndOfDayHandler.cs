using Bank.Common;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Lending.Api.Data;
using Bank.Lending.Api.Logic;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Bank.Lending.Api.Features.EndOfDay;

/// <summary>
/// Lending's end of day for DayClosed(D) (stage 5 file), per active loan in its own scope and transaction, in this order:
/// <list type="number">
/// <item>InstallmentDue for every unpaid item due on or before D without an open collection (items due today, and the
/// daily retry of overdue ones after a PaymentFailed closed the previous attempt; ADR 0005);</item>
/// <item>one day of penalties (Penalties.ForDay) and PenaltyAccrued when it added any;</item>
/// <item>the classification (Classification.For) and LoanClassificationChanged when it changed.</item>
/// </list>
/// (loan, D) is recorded in loan_day_run, so a loan's day runs once and penalties are never charged twice. Then
/// DayCompleted(D, "lending"), once per date (end_of_day_run). The class name is an inbox key.
/// </summary>
public sealed class LendingEndOfDayHandler(LendingDbContext db, IEventPublisher publisher, IServiceScopeFactory scopes, TimeProvider time)
    : IEventHandler<DayClosed>
{
    public const string ServiceName = "lending";

    public async Task HandleAsync(DayClosed e, CancellationToken ct)
    {
        if (await db.EndOfDayRuns.AnyAsync(r => r.Date == e.Date, ct))
        {
            return;
        }

        var loanIds = await db.Loans.AsNoTracking().Where(l => l.Status == LoanStatus.Active).OrderBy(l => l.Id).Select(l => l.Id).ToListAsync(ct);
        foreach (var loanId in loanIds)
        {
            for (var attempt = 1; ; attempt++)
            {
                await using var scope = scopes.CreateAsyncScope();
                try
                {
                    await RunLoanAsync(scope.ServiceProvider, loanId, e.Date, ct);
                    break;
                }
                catch (DbUpdateConcurrencyException) when (attempt == 1)
                {
                    // A payment for this loan was allocated meanwhile: run its day once more on fresh data.
                }
            }
        }

        db.EndOfDayRuns.Add(new EndOfDayRun { Date = e.Date, CreatedAt = time.GetUtcNow(), PerformedBy = PerformedBy.System });
        await publisher.PublishAsync(new DayCompleted(Guid.NewGuid(), e.Date, PerformedBy.System, e.Date, ServiceName), e.Date.ToString("yyyy-MM-dd"), ct);
    }

    private static async Task RunLoanAsync(IServiceProvider services, Guid loanId, DateOnly date, CancellationToken ct)
    {
        var db = services.GetRequiredService<LendingDbContext>();
        var publisher = services.GetRequiredService<IEventPublisher>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        if (await db.LoanDayRuns.AnyAsync(r => r.LoanId == loanId && r.Date == date, ct))
        {
            return;
        }

        var loan = await db.Loans.Include(l => l.Schedule).SingleAsync(l => l.Id == loanId, ct);
        var key = loan.Id.ToString();
        var rows = loan.Schedule.OrderBy(i => i.Number).ToList();

        // 1. Ask Accounts to collect, at most one open request per item, and none while a payoff is pending.
        if (loan.PendingPayoffCollectionId is null)
        {
            foreach (var row in rows.Where(r => r.DueDate <= date && r.Status != ScheduleItemStatus.Paid && r.PendingCollectionId is null))
            {
                row.PendingCollectionId = Guid.CreateVersion7();
                row.PerformedBy = PerformedBy.System;
                var unpaid = row.ToLogic(loan.Currency).Unpaid;
                await publisher.PublishAsync(
                    new InstallmentDue(Guid.NewGuid(), date, PerformedBy.System, loan.Id, row.PendingCollectionId.Value, row.Number,
                        loan.RepaymentAccountId, unpaid, row.DueDate),
                    key, ct);
            }
        }

        // 2. One day of penalties, except while a payoff is pending: the payoff settles the quoted amount (review of 2026-10-31).
        var items = rows.Select(r => r.ToLogic(loan.Currency)).ToList();
        var before = items.Sum(i => i.PenaltyDue.Amount);
        if (loan.PendingPayoffCollectionId is null)
        {
            items = [.. Penalties.ForDay(items, loan.Terms, date)];
        }

        var added = items.Sum(i => i.PenaltyDue.Amount) - before;
        foreach (var (row, item) in rows.Zip(items))
        {
            row.Apply(item);
        }

        if (added > 0m)
        {
            await publisher.PublishAsync(new PenaltyAccrued(Guid.NewGuid(), date, PerformedBy.System, loan.Id, new Money(added, loan.Currency), date), key, ct);
        }

        // 3. Classification.
        var classification = Classification.For(items, loan.Terms, date);
        if (classification != loan.Classification)
        {
            await publisher.PublishAsync(
                new LoanClassificationChanged(Guid.NewGuid(), date, PerformedBy.System, loan.Id, loan.CustomerId, loan.Classification.ToString(),
                    classification.ToString(), Classification.DaysPastDue(items, date)),
                key, ct);
            loan.Classification = classification;
            loan.PerformedBy = PerformedBy.System;
        }

        // Always writes the loan row: a payoff requested meanwhile conflicts on xmin, and this day reruns once, sees the payoff, and asks for no installment.
        loan.LastEndOfDay = date;
        db.LoanDayRuns.Add(new LoanDayRun { LoanId = loan.Id, Date = date, CreatedAt = now, PerformedBy = PerformedBy.System });
        await db.SaveChangesAsync(ct);
    }
}
