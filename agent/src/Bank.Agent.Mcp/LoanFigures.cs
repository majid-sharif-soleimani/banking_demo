using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bank.Agent.Mcp;

/// <summary>
/// A loan's numbers without its schedule. A loan of 60 installments is about 10,000 tokens, and the model pays for them
/// again on every later call of the run, while most questions need a handful of numbers (agent ADR 0011).
///
/// It adds up what the bank answered, in decimal, and decides nothing: what is overdue, what a payment covers and what
/// a payoff costs stay with the bank. "Unpaid" is what is due minus what is paid, item by item, and "overdue" is due
/// before the business date and not fully paid, as Lending defines it for its own overdue amount.
/// </summary>
public static class LoanFigures
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public sealed record Money(decimal Amount, string Currency);

    public sealed record Item(
        int Number, DateOnly DueDate, Money PrincipalDue, Money InterestDue, Money PenaltyDue, Money PaidPrincipal, Money PaidInterest,
        Money PaidPenalty, string Status);

    public sealed record Loan(
        Guid Id, Guid CustomerId, string ProductCode, Money Principal, DateOnly DisbursedOn, string Status, string Classification,
        Money OutstandingPrincipal, Money OverdueAmount, int DaysPastDue, Item[] Schedule);

    public sealed record Parts(Money Principal, Money Interest, Money Penalty, Money Total);

    public sealed record Installment(int Number, DateOnly DueDate, string Status, bool Overdue, Parts Due, Parts Unpaid);

    public sealed record Counts(int Total, int Paid, int Open, int Overdue);

    public sealed record Summary(
        Guid Id, Guid CustomerId, string ProductCode, string Status, string Classification, DateOnly DisbursedOn, DateOnly? LastDueDate,
        Money Principal, Money OutstandingPrincipal, Money OverdueAmount, int DaysPastDue, Counts Installments,
        Parts PaidSoFar, Parts UnpaidOnSchedule, Installment? OldestOpen, Installment? NextNotDue, Installment? Asked);

    /// <param name="loansJson">The bank's answer to GET /loans: an array of loans with their schedules.</param>
    /// <param name="today">The business date.</param>
    /// <param name="loanId">Only this loan; otherwise all of them.</param>
    /// <param name="installmentNumber">Also give the amounts of this installment.</param>
    public static string Summarize(string loansJson, DateOnly today, Guid? loanId = null, int? installmentNumber = null)
    {
        var loans = JsonSerializer.Deserialize<Loan[]>(loansJson, Json) ?? [];
        return JsonSerializer.Serialize(loans.Where(l => loanId is null || l.Id == loanId).Select(l => Of(l, today, installmentNumber)), Json);
    }

    public static Summary Of(Loan loan, DateOnly today, int? installmentNumber = null)
    {
        var items = loan.Schedule.OrderBy(i => i.Number).ToList();
        var currency = loan.Principal.Currency;
        var open = items.Where(i => Unpaid(i) > 0m).ToList();
        Installment? Show(Item? item) => item is null ? null : new Installment(
            item.Number, item.DueDate, item.Status, item.DueDate < today && Unpaid(item) > 0m,
            PartsOf(currency, item.PrincipalDue.Amount, item.InterestDue.Amount, item.PenaltyDue.Amount),
            PartsOf(currency, item.PrincipalDue.Amount - item.PaidPrincipal.Amount, item.InterestDue.Amount - item.PaidInterest.Amount, item.PenaltyDue.Amount - item.PaidPenalty.Amount));

        return new Summary(
            loan.Id, loan.CustomerId, loan.ProductCode, loan.Status, loan.Classification, loan.DisbursedOn, items.LastOrDefault()?.DueDate,
            loan.Principal, loan.OutstandingPrincipal, loan.OverdueAmount, loan.DaysPastDue,
            new Counts(items.Count, items.Count - open.Count, open.Count, open.Count(i => i.DueDate < today)),
            PartsOf(currency, items.Sum(i => i.PaidPrincipal.Amount), items.Sum(i => i.PaidInterest.Amount), items.Sum(i => i.PaidPenalty.Amount)),
            PartsOf(currency, items.Sum(i => i.PrincipalDue.Amount - i.PaidPrincipal.Amount), items.Sum(i => i.InterestDue.Amount - i.PaidInterest.Amount), items.Sum(i => i.PenaltyDue.Amount - i.PaidPenalty.Amount)),
            Show(open.FirstOrDefault()),
            Show(open.FirstOrDefault(i => i.DueDate >= today)),
            Show(items.FirstOrDefault(i => i.Number == installmentNumber)));
    }

    private static decimal Unpaid(Item i) =>
        i.PrincipalDue.Amount + i.InterestDue.Amount + i.PenaltyDue.Amount - i.PaidPrincipal.Amount - i.PaidInterest.Amount - i.PaidPenalty.Amount;

    private static Parts PartsOf(string currency, decimal principal, decimal interest, decimal penalty) =>
        new(new Money(principal, currency), new Money(interest, currency), new Money(penalty, currency), new Money(principal + interest + penalty, currency));

    /// <summary>The business date out of GET /business-days/current.</summary>
    public static DateOnly BusinessDate(string businessDayJson) =>
        JsonDocument.Parse(businessDayJson).RootElement.GetProperty("date").Deserialize<DateOnly>();
}
