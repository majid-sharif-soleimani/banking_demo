using Bank.Contracts;
using Bank.Lending.Api.Logic;

namespace Bank.Lending.UnitTests.Support;

/// <summary>
/// Random but reproducible loans for the invariant tests: each seed always gives the same loan, so a failing case can be
/// rerun alone. Covers month ends (29–31), zero and high rates, one-month to 30-year terms, and 1–4 stage plans.
/// </summary>
public static class LoanGenerator
{
    public const int Cases = 200;

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 1; seed <= Cases; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    public static (LoanTerms Terms, IReadOnlyList<ScheduleItem> Schedule) Annuity(int seed)
    {
        var random = new Random(seed);
        var terms = Base(random, RepaymentType.Annuity) with { TermMonths = random.Next(1, 361) };
        return (terms, Amortization.BuildAnnuity(terms).Value);
    }

    public static (LoanTerms Terms, IReadOnlyList<ScheduleItem> Schedule) Staged(int seed)
    {
        var random = new Random(seed);
        var terms = Base(random, RepaymentType.Staged) with { TermMonths = random.Next(3, 25), MaxStages = 4 };
        var count = random.Next(1, 5);

        // Distinct dates inside the term, in order; shares in whole kronor except the last, which takes the remainder.
        var span = terms.MaturityLimit.DayNumber - terms.DisbursedOn.DayNumber;
        var offsets = Enumerable.Range(1, span).OrderBy(_ => random.Next()).Take(count).Order().ToList();
        var stages = new List<Stage>();
        var left = terms.Principal.Amount;
        for (var i = 0; i < count; i++)
        {
            var share = i == count - 1 ? left : Math.Max(1m, Math.Floor(left * (decimal)random.NextDouble() / 2m));
            stages.Add(new Stage(terms.DisbursedOn.AddDays(offsets[i]), new Money(share, "SEK")));
            left -= share;
        }

        return (terms, Amortization.BuildStaged(terms, new StagePlan(stages)).Value);
    }

    /// <summary>Either kind, alternating by seed.</summary>
    public static (LoanTerms Terms, IReadOnlyList<ScheduleItem> Schedule) Any(int seed) => seed % 2 == 0 ? Annuity(seed) : Staged(seed);

    /// <summary>A date somewhere from disbursement to a little after the last due date.</summary>
    public static DateOnly DateDuring(int seed, LoanTerms terms, IReadOnlyList<ScheduleItem> schedule)
    {
        var random = new Random(seed * 7919);
        var last = schedule[^1].DueDate.AddDays(120);
        return terms.DisbursedOn.AddDays(random.Next(0, last.DayNumber - terms.DisbursedOn.DayNumber + 1));
    }

    private static LoanTerms Base(Random random, RepaymentType type)
    {
        var principal = Math.Round(1_000m + (decimal)random.NextDouble() * 9_999_000m, 2);
        var rate = random.Next(0, 5) == 0 ? 0m : Math.Round((decimal)random.NextDouble() * 0.20m, 4);
        var disbursed = new DateOnly(2026, 1, 1).AddDays(random.Next(0, 4 * 365));
        return new LoanTerms(
            new Money(principal, "SEK"), rate, 12, type, 0.0005m, random.Next(0, 11), 1, 90, 0.01m, new Money(5_000m, "SEK"),
            disbursed, MinorUnits: 2);
    }
}
