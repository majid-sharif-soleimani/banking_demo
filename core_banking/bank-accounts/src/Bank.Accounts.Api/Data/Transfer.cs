using Bank.Contracts;

namespace Bank.Accounts.Api.Data;

/// <summary>
/// A transfer between two accounts in the bank. When the currencies differ, the rate, its source, the day it was
/// published for and the moment it was used are stored (design plan section 2).
/// </summary>
public sealed class Transfer
{
    public Guid Id { get; init; }

    public Guid FromAccountId { get; init; }

    public Guid ToAccountId { get; init; }

    public decimal Amount { get; init; }

    public required string Currency { get; init; }

    public decimal ConvertedAmount { get; init; }

    public required string TargetCurrency { get; init; }

    public decimal? FxRate { get; init; }

    public string? FxSource { get; init; }

    public DateOnly? FxObservedOn { get; init; }

    public DateTimeOffset? FxAt { get; init; }

    /// <summary>The standing order that made it; null for a direct transfer.</summary>
    public Guid? StandingOrderId { get; init; }

    public TransferStatus Status { get; init; }

    public DateOnly BusinessDate { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }

    public Money AmountMoney => new(Amount, Currency);

    public Money ConvertedMoney => new(ConvertedAmount, TargetCurrency);
}

public enum TransferStatus
{
    Completed,
}
