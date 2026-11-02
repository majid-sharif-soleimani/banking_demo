using Bank.Contracts;
using Bank.Lending.Api.Logic;

namespace Bank.Lending.Api.Data;

/// <summary>A loan product. Seeded; its parameters are copied onto each loan at disbursement (design plan section 2).</summary>
public sealed class LoanProduct
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public RepaymentType RepaymentType { get; init; }

    public required string Currency { get; init; }

    public decimal MinAmount { get; init; }

    public decimal MaxAmount { get; init; }

    public int MinTermMonths { get; init; }

    public int MaxTermMonths { get; init; }

    public decimal AnnualRate { get; init; }

    public decimal PenaltyDailyRate { get; init; }

    public int GraceDays { get; init; }

    public int OverdueAfterDays { get; init; }

    public int DefaultAfterDays { get; init; }

    public decimal PayoffMinPercent { get; init; }

    public decimal PayoffCap { get; init; }

    public int? MaxStages { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

public sealed class LoanApplication
{
    public Guid Id { get; init; }

    public Guid CustomerId { get; init; }

    public required string ProductCode { get; init; }

    public decimal RequestedAmount { get; init; }

    public required string Currency { get; init; }

    public int TermMonths { get; init; }

    public Guid RepaymentAccountId { get; init; }

    /// <summary>Staged products only: the agreed stages as JSON (date, share).</summary>
    public string? ProposedStages { get; init; }

    public required string Purpose { get; init; }

    public ApplicationStatus Status { get; set; }

    public Guid? LoanId { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; set; }

    public uint Version { get; set; }

    public List<ApplicationStatusChange> History { get; init; } = [];
}

/// <summary>One step in an application's life; the status history the API returns.</summary>
public sealed class ApplicationStatusChange
{
    public Guid Id { get; init; }

    public Guid ApplicationId { get; init; }

    public ApplicationStatus? FromStatus { get; init; }

    public ApplicationStatus ToStatus { get; init; }

    public string? Reason { get; init; }

    public DateOnly BusinessDate { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

public enum LoanStatus
{
    Active,
    PaidOff,
    PaidOffEarly,
    WrittenOff,
}

/// <summary>A disbursed loan with a copy of its product's parameters, so a product change never alters it.</summary>
public sealed class Loan
{
    public Guid Id { get; init; }

    public Guid ApplicationId { get; init; }

    public Guid CustomerId { get; init; }

    public Guid RepaymentAccountId { get; init; }

    public required string ProductCode { get; init; }

    public decimal Principal { get; init; }

    public required string Currency { get; init; }

    public int MinorUnits { get; init; }

    public RepaymentType RepaymentType { get; init; }

    public decimal AnnualRate { get; init; }

    public int TermMonths { get; init; }

    public decimal PenaltyDailyRate { get; init; }

    public int GraceDays { get; init; }

    public int OverdueAfterDays { get; init; }

    public int DefaultAfterDays { get; init; }

    public decimal PayoffMinPercent { get; init; }

    public decimal PayoffCap { get; init; }

    public int? MaxStages { get; init; }

    public DateOnly DisbursedOn { get; init; }

    public LoanStatus Status { get; set; }

    public LoanClassification Classification { get; set; }

    /// <summary>The open payoff request, if any; at most one at a time.</summary>
    public Guid? PendingPayoffCollectionId { get; set; }

    /// <summary>
    /// The quote the payoff was requested with, settled exactly as quoted (review of 2026-10-31): principal, penalty,
    /// interest (to date + minimum), waived interest. Null without an open payoff.
    /// </summary>
    public decimal? PayoffPrincipal { get; set; }

    public decimal? PayoffPenalty { get; set; }

    public decimal? PayoffInterest { get; set; }

    public decimal? PayoffWaived { get; set; }

    /// <summary>
    /// The last business date this loan's end of day ran. Writing it on the loan row every end of day makes a concurrent
    /// payoff request (which also writes the loan row) conflict on xmin, so the two can never both open a collection.
    /// </summary>
    public DateOnly? LastEndOfDay { get; set; }

    /// <summary>The business date the loan was paid off (normally or early); null while it runs. Reports use it.</summary>
    public DateOnly? ClosedOn { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; set; }

    public uint Version { get; set; }

    public List<ScheduleItemRow> Schedule { get; init; } = [];

    public LoanTerms Terms => new(
        new Money(Principal, Currency), AnnualRate, TermMonths, RepaymentType, PenaltyDailyRate, GraceDays, OverdueAfterDays,
        DefaultAfterDays, PayoffMinPercent, new Money(PayoffCap, Currency), DisbursedOn, MinorUnits, MaxStages);
}

/// <summary>
/// A schedule item as stored. <see cref="PendingCollectionId"/> is the open InstallmentDue for it: Lending never asks
/// Accounts twice for one item before the first request was answered (ADR 0005).
/// </summary>
public sealed class ScheduleItemRow
{
    public Guid LoanId { get; init; }

    public int Number { get; init; }

    public DateOnly DueDate { get; init; }

    public decimal PrincipalDue { get; set; }

    public decimal InterestDue { get; set; }

    public decimal PenaltyDue { get; set; }

    public decimal PaidPrincipal { get; set; }

    public decimal PaidInterest { get; set; }

    public decimal PaidPenalty { get; set; }

    public ScheduleItemStatus Status { get; set; }

    public Guid? PendingCollectionId { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; set; }

    public uint Version { get; set; }

    public ScheduleItem ToLogic(string currency) => new(
        Number, DueDate, new(PrincipalDue, currency), new(InterestDue, currency), new(PenaltyDue, currency),
        new(PaidPrincipal, currency), new(PaidInterest, currency), new(PaidPenalty, currency));

    /// <summary>Copies the amounts of a calculated item back; the status follows from them.</summary>
    public void Apply(ScheduleItem item)
    {
        PrincipalDue = item.PrincipalDue.Amount;
        InterestDue = item.InterestDue.Amount;
        PenaltyDue = item.PenaltyDue.Amount;
        PaidPrincipal = item.PaidPrincipal.Amount;
        PaidInterest = item.PaidInterest.Amount;
        PaidPenalty = item.PaidPenalty.Amount;
        Status = item.Status;
    }
}

public enum PaymentSource
{
    Account,
    Card,
    Payoff,
}

/// <summary>One allocated payment. <see cref="SourceId"/> (the collection or card payment id) is unique: allocated once.</summary>
public sealed class LoanPayment
{
    public Guid Id { get; init; }

    public Guid LoanId { get; init; }

    public Guid SourceId { get; init; }

    public PaymentSource Source { get; init; }

    public decimal Amount { get; init; }

    public decimal Principal { get; init; }

    public decimal Interest { get; init; }

    public decimal Penalty { get; init; }

    public decimal Unallocated { get; init; }

    public DateOnly BusinessDate { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

public sealed class CustomerRef
{
    public Guid CustomerId { get; init; }

    public required string Name { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

/// <summary>Lending's copy of an account, from AccountOpened: selectable as a repayment account (design plan section 6).</summary>
public sealed class AccountRef
{
    public Guid AccountId { get; init; }

    public Guid CustomerId { get; init; }

    public required string Currency { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

public sealed class CurrencyRef
{
    public required string Code { get; init; }

    public int MinorUnits { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

/// <summary>A loan's end of day for one date; (loan, date) is unique, so penalties are charged once per day (stage 3 note).</summary>
public sealed class LoanDayRun
{
    public Guid LoanId { get; init; }

    public DateOnly Date { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

public sealed class EndOfDayRun
{
    public DateOnly Date { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

/// <summary>
/// The portfolio report's projection (ADR 0006): one row per change to the running loans of a product, written in the same
/// transaction as the change. Disbursed: +1 loan, +principal; principal paid: −principal; closed: −1 loan. The report on a
/// date is the sum of the rows on or before it.
/// </summary>
public sealed class PortfolioMovement
{
    public Guid Id { get; init; }

    public Guid LoanId { get; init; }

    public DateOnly BusinessDate { get; init; }

    public required string ProductCode { get; init; }

    public required string Currency { get; init; }

    public int Loans { get; init; }

    public decimal Outstanding { get; init; }

    public ReportMovementReason Reason { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

/// <summary>
/// The arrears report's projection (ADR 0006): like <see cref="PortfolioMovement"/>, but per classification. A
/// reclassification moves the loan and its outstanding principal from the old classification to the new one.
/// </summary>
public sealed class ArrearsMovement
{
    public Guid Id { get; init; }

    public Guid LoanId { get; init; }

    public DateOnly BusinessDate { get; init; }

    public LoanClassification Classification { get; init; }

    public required string Currency { get; init; }

    public int Loans { get; init; }

    public decimal Outstanding { get; init; }

    public ReportMovementReason Reason { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

public enum ReportMovementReason
{
    Disbursed,
    PrincipalPaid,
    Closed,
    Reclassified,
}
