using Bank.Accounts.Api.Data;
using Bank.Accounts.Api.Logic;
using Bank.Contracts;

namespace Bank.Accounts.Api.Features.Movements;

public sealed record EntryResponse(Guid EntryId, Guid AccountId, DateOnly BusinessDate, Money Amount, Money BalanceAfter, string Kind)
{
    public static EntryResponse From(AccountEntry e, string currency) =>
        new(e.Id, e.AccountId, e.BusinessDate, new(e.Amount, currency), new(e.BalanceAfter, currency), e.Kind.ToString());
}

/// <summary>
/// The only way a balance changes: the account's balance and a new entry with the balance after it, in one unit of work.
/// The caller saves; the account's xmin makes a concurrent change fail instead of overwriting.
/// </summary>
public static class Postings
{
    /// <param name="amount">Signed: positive credits, negative debits. The caller has checked that the balance stays ≥ 0.</param>
    public static AccountEntry Post(
        AccountsDbContext db, Account account, decimal amount, int minorUnits, EntryKind kind, Guid? reference, string description,
        DateOnly businessDate, string performedBy, DateTimeOffset now)
    {
        amount = AmountScale.Normalize(amount, minorUnits);
        account.Balance = AmountScale.Normalize(account.Balance + amount, minorUnits);
        account.PerformedBy = performedBy;
        var entry = new AccountEntry
        {
            Id = Guid.CreateVersion7(),
            AccountId = account.Id,
            BusinessDate = businessDate,
            Amount = amount,
            BalanceAfter = account.Balance,
            Kind = kind,
            Reference = reference,
            Description = description,
            CreatedAt = now,
            PerformedBy = performedBy,
        };
        db.Entries.Add(entry);
        return entry;
    }

    /// <summary>
    /// Checks an amount against an account: positive, in the account currency, and in whole minor units. Returns the
    /// problem, or null when the amount is fine.
    /// </summary>
    public static IResult? CheckAmount(Money? amount, string currency, int minorUnits)
    {
        if (amount is null || amount.Amount <= 0m)
        {
            return Problems.Of(StatusCodes.Status400BadRequest, "invalid_amount", "The amount must be greater than zero.");
        }

        if (amount.Currency != currency)
        {
            return Problems.Rule("currency_mismatch", $"The amount must be in the account currency {currency}.");
        }

        return Math.Round(amount.Amount, minorUnits) != amount.Amount
            ? Problems.Of(StatusCodes.Status400BadRequest, "invalid_amount", $"{currency} has {minorUnits} decimals.")
            : null;
    }
}
