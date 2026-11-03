namespace Bank.Agent.Eval;

/// <summary>
/// One conversation of the evaluation set (cases.json): who is talking, what they write, and what a good answer must
/// and must not do. Every check is a rule a program can test, so the same set gives the same kind of score every run.
/// </summary>
public sealed record EvalCase(
    string Id,
    string Role,
    string[] Messages,
    string Why,
    string[]? ToolsCalled = null,
    string[]? ToolsNotCalled = null,
    string[]? Shown = null,
    string[]? AnswerMatches = null,
    string[]? AnswerDoesNotMatch = null,
    string? Language = null);

/// <summary>What one run of a case produced.</summary>
public sealed record EvalRun(IReadOnlyList<string> ToolsCalled, IReadOnlyList<string> Shown, string Answer);
