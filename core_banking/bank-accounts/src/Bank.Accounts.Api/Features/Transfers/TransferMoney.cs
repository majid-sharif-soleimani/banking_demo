using Bank.Accounts.Api.Data;
using Bank.Accounts.Api.Features.Movements;
using Bank.Accounts.Api.Logic;
using Bank.Common;
using Bank.Contracts;
using Bank.Contracts.Events;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Bank.Accounts.Api.Features.Transfers;

/// <summary>Why a transfer could not happen; the endpoint maps it to a 422 and standing orders record it as the reason.</summary>
public enum TransferFailure
{
    None,
    SameAccount,
    InsufficientFunds,
    FxUnavailable,
    AmountTooSmall,
}

/// <summary>
/// Moves money between two accounts in one unit of work: debit the source, credit the target (converted when the
/// currencies differ), store the transfer with its rate, publish MoneyTransferred. The caller saves; both accounts' xmin
/// guard against concurrent changes. Used by POST /transfers and by standing orders at end of day.
/// </summary>
public static class TransferMoney
{
    /// <param name="amount">In the source account's currency, already checked with Postings.CheckAmount.</param>
    public static async Task<(Transfer? Transfer, TransferFailure Failure)> ExecuteAsync(
        AccountsDbContext db, IEventPublisher publisher, IExchangeRateProvider rates, TimeProvider time,
        Account from, Account to, Money amount, DateOnly businessDate, string actor, Guid? standingOrderId, string description,
        CancellationToken ct)
    {
        if (from.Id == to.Id)
        {
            return (null, TransferFailure.SameAccount);
        }

        if (from.Balance < amount.Amount)
        {
            return (null, TransferFailure.InsufficientFunds);
        }

        var now = time.GetUtcNow();
        var targetMinorUnits = await CashMovements.MinorUnitsAsync(db, to.Currency, ct);
        ExchangeRate? rate = null;
        var converted = amount;
        if (from.Currency != to.Currency)
        {
            try
            {
                rate = await rates.GetRateAsync(from.Currency, to.Currency, businessDate, ct);
            }
            catch (ExchangeRateUnavailableException)
            {
                return (null, TransferFailure.FxUnavailable);
            }

            converted = Fx.Convert(amount, rate.Rate, to.Currency, targetMinorUnits);
            if (converted.Amount <= 0m)
            {
                return (null, TransferFailure.AmountTooSmall);
            }
        }

        var sourceMinorUnits = await CashMovements.MinorUnitsAsync(db, from.Currency, ct);
        var transfer = new Transfer
        {
            Id = Guid.CreateVersion7(),
            FromAccountId = from.Id,
            ToAccountId = to.Id,
            Amount = AmountScale.Normalize(amount.Amount, sourceMinorUnits),
            Currency = from.Currency,
            ConvertedAmount = AmountScale.Normalize(converted.Amount, targetMinorUnits),
            TargetCurrency = to.Currency,
            FxRate = rate?.Rate,
            FxSource = rate?.Source,
            FxObservedOn = rate?.ObservedOn,
            FxAt = rate is null ? null : now,
            StandingOrderId = standingOrderId,
            Status = TransferStatus.Completed,
            BusinessDate = businessDate,
            CreatedAt = now,
            PerformedBy = actor,
        };
        db.Transfers.Add(transfer);
        Postings.Post(db, from, -amount.Amount, sourceMinorUnits, EntryKind.TransferOut, transfer.Id, description, businessDate, actor, now);
        Postings.Post(db, to, converted.Amount, targetMinorUnits, EntryKind.TransferIn, transfer.Id, description, businessDate, actor, now);

        await publisher.PublishAsync(
            new MoneyTransferred(Guid.NewGuid(), businessDate, actor, transfer.Id, from.Id, to.Id, transfer.AmountMoney, transfer.ConvertedMoney,
                rate?.Rate.ToString(System.Globalization.CultureInfo.InvariantCulture), rate?.Source, standingOrderId, Fee: null),
            transfer.Id.ToString(), ct);
        return (transfer, TransferFailure.None);
    }
}
