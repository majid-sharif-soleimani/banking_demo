using Bank.Contracts;
using Bank.Lending.Api.Logic;

namespace Bank.Lending.UnitTests.Support;

/// <summary>Loan terms with the seed products' parameters (implementation-decisions, "Loan products").</summary>
public static class Loans
{
    public static Money Sek(decimal amount) => new(amount, "SEK");

    /// <summary>CAR: annuity, 6.95%, penalty 0.05%/day, grace 5, overdue after 1, default after 90, payoff 1% capped at 5,000.</summary>
    public static LoanTerms Car(decimal principal = 200_000m, int months = 36, DateOnly? disbursedOn = null) => new(
        Sek(principal), 0.0695m, months, RepaymentType.Annuity, 0.0005m, 5, 1, 90, 0.01m, Sek(5_000m),
        disbursedOn ?? new DateOnly(2026, 1, 15), MinorUnits: 2);

    /// <summary>MORTGAGE: annuity, 4.25%, penalty 0.03%/day, grace 5, overdue after 1, default after 90, payoff 1% capped at 20,000.</summary>
    public static LoanTerms Mortgage(decimal principal = 2_000_000m, int months = 360) => new(
        Sek(principal), 0.0425m, months, RepaymentType.Annuity, 0.0003m, 5, 1, 90, 0.01m, Sek(20_000m),
        new DateOnly(2026, 1, 15), MinorUnits: 2);

    /// <summary>PURCHASE: staged, 8.50%, penalty 0.05%/day, grace 5, overdue after 1, default after 90, payoff 1% capped at 3,000, at most 4 stages.</summary>
    public static LoanTerms Purchase(decimal principal = 100_000m, int months = 12, DateOnly? disbursedOn = null) => new(
        Sek(principal), 0.085m, months, RepaymentType.Staged, 0.0005m, 5, 1, 90, 0.01m, Sek(3_000m),
        disbursedOn ?? new DateOnly(2026, 1, 15), MinorUnits: 2, MaxStages: 4);

    public static IReadOnlyList<ScheduleItem> Annuity(LoanTerms terms) => Amortization.BuildAnnuity(terms).Value;
}
