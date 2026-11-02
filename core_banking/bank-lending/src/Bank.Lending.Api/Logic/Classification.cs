namespace Bank.Lending.Api.Logic;

public enum LoanClassification
{
    Current,
    Overdue,
    Default,
}

public static class Classification
{
    /// <summary>
    /// Days past due = <paramref name="date"/> − the due date of the oldest item due by then that is not fully paid
    /// (penalty included, ADR 0003); 0 when there is none. At least DefaultAfterDays → Default, at least OverdueAfterDays
    /// → Overdue, otherwise Current. The thresholds come from the loan terms (design plan section 2).
    /// </summary>
    public static LoanClassification For(IReadOnlyList<ScheduleItem> schedule, LoanTerms terms, DateOnly date)
    {
        var daysPastDue = DaysPastDue(schedule, date);
        return daysPastDue >= terms.DefaultAfterDays ? LoanClassification.Default
            : daysPastDue >= terms.OverdueAfterDays ? LoanClassification.Overdue
            : LoanClassification.Current;
    }

    public static int DaysPastDue(IReadOnlyList<ScheduleItem> schedule, DateOnly date) =>
        schedule.Where(i => i.DueDate <= date && !i.IsPaid)
            .Select(i => Amortization.Days(i.DueDate, date))
            .DefaultIfEmpty(0)
            .Max();
}
