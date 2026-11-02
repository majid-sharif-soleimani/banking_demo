using Bank.Contracts;

namespace Bank.Accounts.Api.Data;

/// <summary>
/// An account and its current balance. The balance is stored and changed together with each entry, under the row's
/// xmin (ADR 0002): two concurrent changes cannot both succeed, so the balance can never be overdrawn by a race.
/// </summary>
public sealed class Account
{
    public Guid Id { get; init; }

    /// <summary>9 digits, unique: 8 random plus a Luhn check digit.</summary>
    public required string Number { get; init; }

    public Guid CustomerId { get; init; }

    public AccountType Type { get; init; }

    public required string Currency { get; init; }

    public decimal Balance { get; set; }

    public AccountStatus Status { get; set; }

    /// <summary>The business date the account was opened.</summary>
    public DateOnly OpenedOn { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; set; }

    /// <summary>PostgreSQL's xmin, like every table's Version; checked on every balance change.</summary>
    public uint Version { get; set; }

    public Money BalanceMoney => new(Balance, Currency);
}

public enum AccountType
{
    Current,
    Savings,
}

public enum AccountStatus
{
    Open,
}
