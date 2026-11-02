using Bank.Lending.Api.Data;
using Bank.Lending.Api.Logic;

namespace Bank.Lending.Api.Features.Loans;

/// <summary>
/// Writes the report projections (ADR 0006). Callers add the rows to the same DbContext as the change itself, so the
/// projection is saved in the same transaction and can never disagree with the loan.
/// </summary>
internal static class ReportProjections
{
    /// <summary>A new loan: +1 loan and +principal, as Current.</summary>
    public static void Disbursed(LendingDbContext db, Loan loan, DateOnly date, DateTimeOffset now, string actor)
    {
        Portfolio(db, loan, date, 1, loan.Principal, ReportMovementReason.Disbursed, now, actor);
        Arrears(db, loan, loan.Classification, date, 1, loan.Principal, ReportMovementReason.Disbursed, now, actor);
    }

    /// <summary>Principal paid lowers the outstanding amount in the loan's current classification. Nothing for zero.</summary>
    public static void PrincipalPaid(LendingDbContext db, Loan loan, decimal principal, DateOnly date, DateTimeOffset now, string actor)
    {
        if (principal == 0m)
        {
            return;
        }

        Portfolio(db, loan, date, 0, -principal, ReportMovementReason.PrincipalPaid, now, actor);
        Arrears(db, loan, loan.Classification, date, 0, -principal, ReportMovementReason.PrincipalPaid, now, actor);
    }

    /// <summary>The loan stops running. Call before the classification is reset, so the loan leaves the right class.</summary>
    public static void Closed(LendingDbContext db, Loan loan, DateOnly date, DateTimeOffset now, string actor)
    {
        Portfolio(db, loan, date, -1, 0m, ReportMovementReason.Closed, now, actor);
        Arrears(db, loan, loan.Classification, date, -1, 0m, ReportMovementReason.Closed, now, actor);
    }

    /// <summary>The loan and its outstanding principal move from one classification to another.</summary>
    public static void Reclassified(
        LendingDbContext db, Loan loan, LoanClassification from, LoanClassification to, decimal outstanding, DateOnly date, DateTimeOffset now, string actor)
    {
        Arrears(db, loan, from, date, -1, -outstanding, ReportMovementReason.Reclassified, now, actor);
        Arrears(db, loan, to, date, 1, outstanding, ReportMovementReason.Reclassified, now, actor);
    }

    private static void Portfolio(
        LendingDbContext db, Loan loan, DateOnly date, int loans, decimal outstanding, ReportMovementReason reason, DateTimeOffset now, string actor) =>
        db.PortfolioMovements.Add(new PortfolioMovement
        {
            Id = Guid.CreateVersion7(), LoanId = loan.Id, BusinessDate = date, ProductCode = loan.ProductCode, Currency = loan.Currency,
            Loans = loans, Outstanding = outstanding, Reason = reason, CreatedAt = now, PerformedBy = actor,
        });

    private static void Arrears(
        LendingDbContext db, Loan loan, LoanClassification classification, DateOnly date, int loans, decimal outstanding, ReportMovementReason reason,
        DateTimeOffset now, string actor) =>
        db.ArrearsMovements.Add(new ArrearsMovement
        {
            Id = Guid.CreateVersion7(), LoanId = loan.Id, BusinessDate = date, Classification = classification, Currency = loan.Currency,
            Loans = loans, Outstanding = outstanding, Reason = reason, CreatedAt = now, PerformedBy = actor,
        });
}
