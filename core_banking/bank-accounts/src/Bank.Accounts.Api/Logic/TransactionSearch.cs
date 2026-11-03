using Bank.Common;
using Bank.Contracts;

namespace Bank.Accounts.Api.Logic;

/// <summary>
/// The value filters of a transaction search. Dates are business dates, inclusive. The amount range applies to the size of
/// a movement (its absolute value), in one currency: a search from 10,000 SEK finds a deposit of 15,000 and a withdrawal
/// of 15,000 alike; the movement type tells them apart.
/// </summary>
public sealed record TransactionFilter(DateOnly? From, DateOnly? To, Money? MinAmount, Money? MaxAmount, string? Currency);

/// <summary>Checks a transaction search before it runs (bank-accounts ADR 0004). Pure: today's business date is passed in.</summary>
public static class TransactionSearch
{
    /// <summary>Null when the filter can run; otherwise the first problem, with a code the API returns as 400.</summary>
    public static Error? Validate(TransactionFilter filter, DateOnly today)
    {
        if (filter.From is { } from && filter.To is { } to && from > to)
        {
            return new Error("invalid_period", "'from' must not be after 'to'.");
        }

        if (filter.From > today || filter.To > today)
        {
            return new Error("date_in_future", "Dates must not be after the current business date.");
        }

        if (filter.MinAmount?.Amount < 0m || filter.MaxAmount?.Amount < 0m
            || (filter.MinAmount is { } min && filter.MaxAmount is { } max && min.Amount > max.Amount))
        {
            return new Error("invalid_amount_range", "Amounts are sizes (0 or more) and 'minAmount' must not be above 'maxAmount'.");
        }

        var currencies = new[] { filter.Currency, filter.MinAmount?.Currency, filter.MaxAmount?.Currency }.OfType<string>().Distinct().Count();
        return currencies > 1
            ? new Error("currency_mismatch", "'minAmount', 'maxAmount' and 'currency' must name the same currency.")
            : null;
    }

    /// <summary>The currency to search in: the filter's, or else the amounts'. Null means every currency.</summary>
    public static string? Currency(TransactionFilter filter) => filter.Currency ?? filter.MinAmount?.Currency ?? filter.MaxAmount?.Currency;
}
