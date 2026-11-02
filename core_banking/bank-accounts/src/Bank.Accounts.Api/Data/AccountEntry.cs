namespace Bank.Accounts.Api.Data;

/// <summary>
/// One movement on an account, append-only. <see cref="Amount"/> is signed (credit positive, debit negative) and
/// <see cref="BalanceAfter"/> is the account balance right after it, so statements and balance-at-date are read from
/// entries alone (design plan section 2).
/// </summary>
public sealed class AccountEntry
{
    public Guid Id { get; init; }

    /// <summary>Identity column: the order of entries, also within one business date.</summary>
    public long Sequence { get; init; }

    public Guid AccountId { get; init; }

    public DateOnly BusinessDate { get; init; }

    public decimal Amount { get; init; }

    public decimal BalanceAfter { get; init; }

    public EntryKind Kind { get; init; }

    /// <summary>What caused it: a transfer or standing order id, for example; null for cash.</summary>
    public Guid? Reference { get; init; }

    public required string Description { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    /// <summary>PostgreSQL's xmin, like every table's Version. Entries are only inserted, so it is never checked.</summary>
    public uint Version { get; set; }
}

public enum EntryKind
{
    Deposit,
    Withdrawal,
    TransferOut,
    TransferIn,
    LoanDisbursement,
    LoanRepayment,
}
