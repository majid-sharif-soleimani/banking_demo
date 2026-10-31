using Bank.Contracts;
using Bank.Lending.Api.Data;
using Bank.Lending.Api.Logic;

namespace Bank.Lending.Api.Features.Loans;

public sealed record ScheduleItemResponse(
    int Number, DateOnly DueDate, Money PrincipalDue, Money InterestDue, Money PenaltyDue, Money PaidPrincipal, Money PaidInterest,
    Money PaidPenalty, string Status);

public sealed record LoanResponse(
    Guid Id, Guid ApplicationId, Guid CustomerId, string ProductCode, Money Principal, Guid RepaymentAccountId, DateOnly DisbursedOn,
    string Status, string Classification, Money OutstandingPrincipal, Money OverdueAmount, int DaysPastDue,
    IReadOnlyList<ScheduleItemResponse> Schedule)
{
    /// <param name="today">The business date: overdue means due before it and not fully paid.</param>
    public static LoanResponse From(Loan loan, DateOnly today)
    {
        var items = loan.Schedule.OrderBy(i => i.Number).Select(i => i.ToLogic(loan.Currency)).ToList();
        Money Sum(IEnumerable<Money> amounts) => amounts.Aggregate(Money.Zero(loan.Currency), (a, b) => a + b);
        return new LoanResponse(
            loan.Id, loan.ApplicationId, loan.CustomerId, loan.ProductCode, new Money(loan.Principal, loan.Currency), loan.RepaymentAccountId,
            loan.DisbursedOn, loan.Status.ToString(), loan.Classification.ToString(),
            Sum(items.Select(i => i.UnpaidPrincipal)),
            Sum(items.Where(i => i.DueDate < today && !i.IsPaid).Select(i => i.Unpaid)),
            Logic.Classification.DaysPastDue(items, today),
            [.. items.Select(i => new ScheduleItemResponse(i.Number, i.DueDate, i.PrincipalDue, i.InterestDue, i.PenaltyDue, i.PaidPrincipal,
                i.PaidInterest, i.PaidPenalty, i.Status.ToString()))]);
    }
}
