using System.Text.RegularExpressions;

namespace Bank.Agent.Eval;

/// <summary>Grades one run against its case. Each failed rule is one line in the report; no lines means passed.</summary>
public static class Grader
{
    public static IReadOnlyList<string> Check(EvalCase c, EvalRun run)
    {
        var failures = new List<string>();
        failures.AddRange((c.ToolsCalled ?? []).Where(t => !run.ToolsCalled.Contains(t)).Select(t => $"did not call {t}"));
        failures.AddRange((c.ToolsNotCalled ?? []).Where(run.ToolsCalled.Contains).Select(t => $"called {t}"));
        failures.AddRange((c.Shown ?? []).Where(s => !run.Shown.Contains(s)).Select(s => $"did not show {s}"));
        failures.AddRange((c.AnswerMatches ?? []).Where(p => !Regex.IsMatch(run.Answer, p, RegexOptions.IgnoreCase)).Select(p => $"answer lacks /{p}/"));
        failures.AddRange((c.AnswerDoesNotMatch ?? []).Where(p => Regex.IsMatch(run.Answer, p, RegexOptions.IgnoreCase)).Select(p => $"answer has /{p}/"));
        if (c.Language is { } wanted && LanguageOf(run.Answer) != wanted)
        {
            failures.Add($"answer is in {LanguageOf(run.Answer)}, not {wanted}");
        }

        return failures;
    }

    /// <summary>
    /// A rough guess, good enough for "did it answer in the person's language": mostly Arabic-script letters is
    /// Persian; Swedish letters or common Swedish words is Swedish; anything else is English.
    /// </summary>
    public static string LanguageOf(string text)
    {
        var letters = text.Where(char.IsLetter).ToList();
        if (letters.Count > 0 && letters.Count(ch => ch is >= '؀' and <= 'ۿ') * 2 > letters.Count)
        {
            return "fa";
        }

        return Regex.IsMatch(text, @"[åäö]|\b(och|är|du|ditt|dina|konto|saldo|inte)\b", RegexOptions.IgnoreCase) ? "sv" : "en";
    }
}
