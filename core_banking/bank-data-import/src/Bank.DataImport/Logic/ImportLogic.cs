using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Bank.Contracts;

namespace Bank.DataImport.Logic;

/// <summary>A source customer as far as selection is concerned.</summary>
public sealed record Candidate(string Id, bool HasLoan, bool HasLatePayments, bool HasStandingOrder);

/// <summary>How many customers to import, and the minimum number with each trait (stage 7 file: 100, 30, 10, 15).</summary>
public sealed record SelectionTargets(int Total, int WithLoan, int WithLatePayments, int WithStandingOrder);

/// <summary>
/// Picks the customers to import (stage 7 file, step 2): first enough with late payments, then enough with a loan, then
/// enough with standing orders, then random ones until the total. Every draw uses one Random with a fixed seed, so the
/// same input and seed always give the same customers.
/// </summary>
public static class Selection
{
    public static IReadOnlyList<string> Select(IReadOnlyList<Candidate> candidates, SelectionTargets targets, int seed)
    {
        var random = new Random(seed);
        var pool = candidates.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        var chosen = new List<Candidate>();

        void TakeUntil(Func<Candidate, bool> trait, int minimum)
        {
            var missing = minimum - chosen.Count(trait);
            foreach (var candidate in pool.Where(c => trait(c) && !chosen.Contains(c)).OrderBy(_ => random.Next()).Take(Math.Max(0, missing)))
            {
                chosen.Add(candidate);
            }
        }

        TakeUntil(c => c.HasLatePayments, targets.WithLatePayments);
        TakeUntil(c => c.HasLoan, targets.WithLoan);
        TakeUntil(c => c.HasStandingOrder, targets.WithStandingOrder);
        TakeUntil(_ => true, targets.Total);
        return [.. chosen.Take(targets.Total).Select(c => c.Id)];
    }
}

/// <summary>
/// Moves history into the bank's calendar (stage 7 file, step 3): only the last <c>maxYears</c> are kept, and the first
/// kept day becomes Operations:StartDate, so the latest event lands on StartDate + the import span.
/// </summary>
public sealed record DateShift(DateOnly FirstKept, int Days)
{
    public static DateShift Compute(DateOnly earliest, DateOnly latest, DateOnly startDate, int maxYears)
    {
        var cutoff = latest.AddYears(-maxYears);
        var firstKept = earliest > cutoff ? earliest : cutoff;
        return new DateShift(firstKept, startDate.DayNumber - firstKept.DayNumber);
    }

    public bool Keeps(DateOnly date) => date >= FirstKept;

    public DateOnly Apply(DateOnly date) => date.AddDays(Days);
}

public static class Amounts
{
    /// <summary>Source amount × factor in SEK (stage 7 file, step 4; default factor 10.0), rounded AwayFromZero to öre.</summary>
    public static Money ToSek(decimal amount, decimal factor) =>
        new(Math.Round(amount * factor, 2, MidpointRounding.AwayFromZero), "SEK");
}

public static class LoanTypes
{
    /// <summary>
    /// The dataset's loan type text → product code by whole keyword ("Credit card" is not a car); anything else is
    /// PURCHASE (stage 7 file, step 4).
    /// </summary>
    public static string Map(string? text)
    {
        var words = Words.Of(text);
        return words.Overlaps(["car", "auto", "vehicle"]) ? "CAR"
            : words.Overlaps(["mortgage", "home", "house"]) ? "MORTGAGE"
            : "PURCHASE";
    }
}

/// <summary>
/// The dataset's account type → Accounts' type. Only the obvious words are mapped; anything else is null, and the row is
/// skipped and reported rather than guessed (stage 7 plan: skip and report, never invent).
/// </summary>
public static class AccountTypes
{
    public static string? Map(string? text)
    {
        var words = Words.Of(text);
        return words.Overlaps(["savings", "saving"]) ? "Savings"
            : words.Overlaps(["current", "checking"]) ? "Current"
            : null;
    }
}

/// <summary>The dataset's standing-order frequency → one Accounts supports (Weekly, Monthly, Quarterly), or null to skip and report.</summary>
public static class Frequencies
{
    public static string? Map(string? text) => (text ?? "").Trim().ToLowerInvariant() switch
    {
        "weekly" => "Weekly",
        "monthly" => "Monthly",
        "quarterly" => "Quarterly",
        _ => null,
    };
}

internal static class Words
{
    public static HashSet<string> Of(string? text) =>
        [.. System.Text.RegularExpressions.Regex.Split((text ?? "").ToLowerInvariant(), "[^a-z]+").Where(w => w.Length > 0)];
}

public static class IdempotencyKeys
{
    /// <summary>A stable key per source row and action (stage 7 file, step 6): re-running the import repeats the same keys.</summary>
    public static string For(params string[] parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts))))[..40].ToLowerInvariant();
}

public sealed record PlannedStage(DateOnly DueDate, Money Share);

public static class StagePlanner
{
    /// <summary>
    /// Equal stages for a PURCHASE loan: one every quarter at most (term / 3), never more than the product allows, the last
    /// on the maturity date; shares in öre with the remainder in the last one.
    /// </summary>
    public static IReadOnlyList<PlannedStage> Equal(Money principal, DateOnly disbursedOn, int termMonths, int maxStages)
    {
        var count = Math.Clamp(termMonths / 3, 1, maxStages);
        var share = Math.Floor(principal.Amount / count * 100m) / 100m;
        return [.. Enumerable.Range(1, count).Select(i => new PlannedStage(
            disbursedOn.AddMonths(termMonths * i / count),
            new Money(i == count ? principal.Amount - share * (count - 1) : share, principal.Currency)))];
    }
}

internal static class Invariant
{
    public static decimal Decimal(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
}
