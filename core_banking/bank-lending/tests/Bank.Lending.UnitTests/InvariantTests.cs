using Bank.Contracts;
using Bank.Lending.Api.Logic;
using Bank.Lending.UnitTests.Support;

namespace Bank.Lending.UnitTests;

/// <summary>
/// Properties that must hold for every loan, checked on <see cref="LoanGenerator.Cases"/> generated loans each. Where the
/// examples in the other classes pin exact numbers, these pin the rules that hold whatever the numbers are.
/// </summary>
public class InvariantTests
{
    [Theory]
    [MemberData(nameof(LoanGenerator.Seeds), MemberType = typeof(LoanGenerator))]
    public void Schedule_AnyLoan_PrincipalSumsExactlyToTheLoan(int seed)
    {
        var (terms, schedule) = LoanGenerator.Any(seed);

        schedule.Sum(i => i.PrincipalDue.Amount).ShouldBe(terms.Principal.Amount);
    }

    [Theory]
    [MemberData(nameof(LoanGenerator.Seeds), MemberType = typeof(LoanGenerator))]
    public void Schedule_AnyLoan_AmountsAreNonNegativeRoundedAndDatesIncrease(int seed)
    {
        var (terms, schedule) = LoanGenerator.Any(seed);

        schedule.ShouldAllBe(i => i.PrincipalDue.Amount >= 0m && i.InterestDue.Amount >= 0m);
        schedule.ShouldAllBe(i => i.PrincipalDue.Amount == Math.Round(i.PrincipalDue.Amount, 2)
                                  && i.InterestDue.Amount == Math.Round(i.InterestDue.Amount, 2));
        schedule.Zip(schedule.Skip(1)).ShouldAllBe(pair => pair.First.DueDate < pair.Second.DueDate);
        schedule[0].DueDate.ShouldBeGreaterThan(terms.DisbursedOn);
    }

    [Theory]
    [MemberData(nameof(LoanGenerator.Seeds), MemberType = typeof(LoanGenerator))]
    public void Annuity_AnyLoan_EveryItemButTheLastTotalsThePayment(int seed)
    {
        var (_, schedule) = LoanGenerator.Annuity(seed);
        var payment = schedule[0].PrincipalDue.Amount + schedule[0].InterestDue.Amount;

        schedule.Take(schedule.Count - 1).ShouldAllBe(i => i.PrincipalDue.Amount + i.InterestDue.Amount == payment);
    }

    [Theory]
    [MemberData(nameof(LoanGenerator.Seeds), MemberType = typeof(LoanGenerator))]
    public void Allocation_AnyPayment_NeverPaysMoreThanDueAndLosesNoMoney(int seed)
    {
        var (terms, schedule) = LoanGenerator.Any(seed);
        var date = LoanGenerator.DateDuring(seed, terms, schedule);
        var withPenalty = Penalties.ForDay(schedule, terms, date);
        var amount = new Money(Math.Round(1m + (decimal)new Random(seed).NextDouble() * terms.Principal.Amount, 2), "SEK");

        var result = Allocation.Apply(withPenalty, amount, date).Value;

        result.Schedule.ShouldAllBe(i => i.PaidPrincipal <= i.PrincipalDue && i.PaidInterest <= i.InterestDue && i.PaidPenalty <= i.PenaltyDue);
        (result.Allocations.Sum(a => a.Total.Amount) + result.Unallocated.Amount).ShouldBe(amount.Amount);
        result.Unallocated.Amount.ShouldBeGreaterThanOrEqualTo(0m);
        result.Schedule.Where(i => i.DueDate > date).ShouldAllBe(i => i.PaidPrincipal.Amount == 0m && i.PaidInterest.Amount == 0m);
    }

    [Theory]
    [MemberData(nameof(LoanGenerator.Seeds), MemberType = typeof(LoanGenerator))]
    public void Penalties_AnyDay_NeverDecreaseAndOnlyAfterGrace(int seed)
    {
        var (terms, schedule) = LoanGenerator.Any(seed);
        var date = LoanGenerator.DateDuring(seed, terms, schedule);

        var after = Penalties.ForDay(schedule, terms, date);

        after.Zip(schedule).ShouldAllBe(pair => pair.First.PenaltyDue >= pair.Second.PenaltyDue);
        after.Zip(schedule).Where(pair => pair.First.PenaltyDue > pair.Second.PenaltyDue)
            .ShouldAllBe(pair => pair.Second.DueDate.AddDays(terms.GraceDays) < date);
    }

    [Theory]
    [MemberData(nameof(LoanGenerator.Seeds), MemberType = typeof(LoanGenerator))]
    public void Payoff_AnyDate_IsAtLeastThePrincipalAndAtMostKeepingTheLoan(int seed)
    {
        var (terms, schedule) = LoanGenerator.Any(seed);
        var date = LoanGenerator.DateDuring(seed, terms, schedule);

        var quote = EarlyPayoff.Quote(schedule, terms, date);

        quote.Total.ShouldBeGreaterThanOrEqualTo(quote.OutstandingPrincipal);
        quote.WaivedInterest.Amount.ShouldBeGreaterThanOrEqualTo(0m);
        quote.MinimumInterest.ShouldBeLessThanOrEqualTo(terms.PayoffCap);
        // Paying off early never costs more than paying every remaining installment as scheduled.
        quote.Total.Amount.ShouldBeLessThanOrEqualTo(schedule.Sum(i => i.Unpaid.Amount));
        (quote.InterestToDate + quote.MinimumInterest + quote.WaivedInterest).Amount
            .ShouldBe(schedule.Sum(i => i.UnpaidInterest.Amount));
    }

    [Theory]
    [MemberData(nameof(LoanGenerator.Seeds), MemberType = typeof(LoanGenerator))]
    public void Classification_AnyDate_GetsWorseOnlyAsTimePasses(int seed)
    {
        var (terms, schedule) = LoanGenerator.Any(seed);
        var date = LoanGenerator.DateDuring(seed, terms, schedule);

        var today = Classification.For(schedule, terms, date);
        var later = Classification.For(schedule, terms, date.AddDays(30));

        ((int)later).ShouldBeGreaterThanOrEqualTo((int)today);
    }
}
