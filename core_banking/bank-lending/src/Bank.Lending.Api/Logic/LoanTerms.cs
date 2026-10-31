using Bank.Contracts;

namespace Bank.Lending.Api.Logic;

public enum RepaymentType
{
    /// <summary>Equal monthly installments; monthly rate = annual rate / 12 (30/360).</summary>
    Annuity,

    /// <summary>Agreed principal shares on agreed dates; interest Actual/365 since the previous due date.</summary>
    Staged,
}

/// <summary>
/// Everything a calculation needs about one loan. The parameters are copied from the product at disbursement (design
/// plan section 2), so nothing in Logic/ knows a product or a hard-coded rate. Rates and percentages are fractions:
/// 6.95% is 0.0695.
/// </summary>
/// <param name="MinorUnits">Digits of the loan currency, for MoneyRounding (2 for SEK).</param>
/// <param name="MaxStages">The product's maximum number of stages; only used for staged loans.</param>
/// <param name="FirstDueDate">Defaults to one month after <paramref name="DisbursedOn"/>, same day of month.</param>
public sealed record LoanTerms(
    Money Principal,
    decimal AnnualRate,
    int TermMonths,
    RepaymentType RepaymentType,
    decimal PenaltyDailyRate,
    int GraceDays,
    int OverdueAfterDays,
    int DefaultAfterDays,
    decimal PayoffMinPercent,
    Money PayoffCap,
    DateOnly DisbursedOn,
    int MinorUnits,
    int? MaxStages = null,
    DateOnly? FirstDueDate = null)
{
    public string Currency => Principal.Currency;

    /// <summary>DateOnly.AddMonths keeps the day, or takes the month's last day when it is shorter (31 Jan → 28 Feb).</summary>
    public DateOnly EffectiveFirstDueDate => FirstDueDate ?? DisbursedOn.AddMonths(1);

    /// <summary>The last date a staged loan may fall due: disbursement plus the term.</summary>
    public DateOnly MaturityLimit => DisbursedOn.AddMonths(TermMonths);
}

/// <summary>One agreed stage of a staged loan: on <paramref name="DueDate"/> this share of the principal is repaid.</summary>
public sealed record Stage(DateOnly DueDate, Money PrincipalShare);

/// <summary>The stages agreed at approval. The shares must sum to the principal, dates strictly increasing.</summary>
public sealed record StagePlan(IReadOnlyList<Stage> Stages);
