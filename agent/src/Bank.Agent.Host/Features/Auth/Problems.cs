using Bank.Agent.Host.Logic;

namespace Bank.Agent.Host.Features.Auth;

/// <summary>The problem details every feature answers with; the code extension is what the chat UI reads.</summary>
public static class Problems
{
    public static IResult Of(int status, string code, string title, string? detail = null) =>
        Results.Problem(statusCode: status, title: title, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    public static IResult InvalidCredentials() => Of(StatusCodes.Status401Unauthorized, "invalid_credentials", "Wrong username or password.");

    public static IResult BankUnavailable() =>
        Of(StatusCodes.Status503ServiceUnavailable, "bank_unavailable", "The bank could not be reached. Try again in a moment.");

    public static IResult MissingSession() => Of(StatusCodes.Status401Unauthorized, "missing_session", "Log in first.");

    public static IResult SessionEnded(ChatCloseReason reason) => Of(StatusCodes.Status401Unauthorized, SessionEnd.Code(reason), Title(reason));

    public static IResult MissingClientHeader() =>
        Of(StatusCodes.Status403Forbidden, "missing_client_header", "Requests must come from the chat UI.");

    public static string Title(ChatCloseReason reason) => reason switch
    {
        ChatCloseReason.SessionReplaced => "You logged in somewhere else.",
        ChatCloseReason.SessionExpired => "The session ended because nothing happened for a while.",
        ChatCloseReason.Logout => "You logged out.",
        _ => "A newer login ended this chat.",
    };
}
