using Bank.Agent.Host.Features.Chats;
using Bank.Agent.Host.Logic;

namespace Bank.Agent.Host.Features.Auth;

public sealed record LoginRequest(string? Username, string? Password);

/// <summary>What the chat UI may know about the session: never the token.</summary>
public sealed record SessionInfo(string Role, string Name, Guid ChatId);

/// <summary>
/// The backend for frontend (phase 2 design plan, decision A9 and flow 4.1): the login form posts here, never to the
/// model. The bank token stays in <see cref="HostSessions"/>; the browser gets an opaque HttpOnly cookie.
/// </summary>
public static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/auth").WithTags("Auth");
        auth.MapPost("/login", LoginAsync)
            .WithSummary("Log in through identity. Opens a new, empty chat and sets the session cookie.")
            .Produces<SessionInfo>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        auth.MapPost("/logout", LogoutAsync)
            .WithSummary("End the session, close its chat and clear the cookie.")
            .Produces(StatusCodes.Status204NoContent);
        auth.MapGet("/me", MeAsync)
            .WithSummary("The current session, or why there is none.")
            .Produces<SessionInfo>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, HttpContext http, BankGateway bank, HostSessions sessions, ChatStore chats, CancellationToken ct)
    {
        // Blank fields get the same answer as a wrong password, without asking the bank.
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return Problems.InvalidCredentials();
        }

        var outcome = await bank.LoginAsync(request.Username.Trim(), request.Password, ct);
        if (outcome is LoginOutcome.Refused)
        {
            return Problems.InvalidCredentials();
        }

        if (outcome is not LoginOutcome.Success(var token, var principal))
        {
            return Problems.BankUnavailable();
        }

        // This browser's previous login, and every other host session of the user: identity has already replaced their
        // bank sessions. Idle sessions of anyone are cleaned up on the way.
        if (sessions.End(SessionCookie.Read(http.Request)) is { } previous)
        {
            // The same browser logs in again, maybe as someone else: its old chat and bank session end here.
            await chats.CloseAsync(previous.ChatId, ChatCloseReason.NewLogin, ct);
            if (previous.Principal.Username != principal.Username)
            {
                await bank.LogoutAsync(previous.BankToken, ct);
            }
        }

        foreach (var ended in sessions.EndForNewLogin(principal.Username))
        {
            await chats.CloseAsync(ended.ChatId,
                ended.Principal.Username == principal.Username ? ChatCloseReason.NewLogin : ChatCloseReason.SessionExpired, ct);
        }

        await chats.CloseOpenChatsOfAsync(principal.Username, ChatCloseReason.NewLogin, ct);
        var chat = await chats.OpenAsync(principal, ct);
        var session = sessions.Start(token, principal, chat.Id);
        SessionCookie.Write(http.Response, session.Id);
        return Results.Ok(Info(session));
    }

    private static async Task<IResult> LogoutAsync(HttpContext http, BankGateway bank, HostSessions sessions, ChatStore chats, CancellationToken ct)
    {
        if (sessions.End(SessionCookie.Read(http.Request)) is { } session)
        {
            await chats.CloseAsync(session.ChatId, ChatCloseReason.Logout, ct);
            await bank.LogoutAsync(session.BankToken, ct);
        }

        SessionCookie.Clear(http.Response);
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(HttpContext http, SessionGuard guard, CancellationToken ct) =>
        await guard.CheckAsync(http, ct) switch
        {
            GuardResult.Allowed(var session) => Results.Ok(Info(session)),
            GuardResult.Denied(var problem, _) => problem,
            var other => throw new InvalidOperationException($"Unexpected {other}"),
        };

    private static SessionInfo Info(HostSession session) =>
        new(session.Principal.Role == Role.Admin ? "admin" : "customer", session.Principal.Name, session.ChatId);
}

public abstract record GuardResult
{
    public sealed record Allowed(HostSession Session) : GuardResult;

    /// <param name="Code">The problem's code, such as missing_session or session_replaced.</param>
    public sealed record Denied(IResult Problem, string Code) : GuardResult;
}

/// <summary>
/// The check before anything that acts for the person: the host session exists and is not idle, and the bank session
/// still works (which also keeps it alive while the person only chats). An ended session closes its chat with the
/// reason identity gave, ends the host session and clears the cookie.
/// </summary>
public sealed class SessionGuard(HostSessions sessions, BankGateway bank, ChatStore chats)
{
    public async Task<GuardResult> CheckAsync(HttpContext http, CancellationToken ct)
    {
        switch (sessions.Use(SessionCookie.Read(http.Request)))
        {
            case SessionLookup.Unknown:
                SessionCookie.Clear(http.Response);
                return new GuardResult.Denied(Problems.MissingSession(), "missing_session");
            case SessionLookup.Idle(var idle):
                await chats.CloseAsync(idle.ChatId, ChatCloseReason.SessionExpired, ct);
                SessionCookie.Clear(http.Response);
                return new GuardResult.Denied(Problems.SessionEnded(ChatCloseReason.SessionExpired), SessionEnd.Code(ChatCloseReason.SessionExpired));
            case SessionLookup.Active(var session):
                switch (await bank.CheckSessionAsync(session.BankToken, ct))
                {
                    case SessionCheck.Valid:
                        return new GuardResult.Allowed(session);
                    case SessionCheck.Ended(var reason):
                        sessions.End(session.Id);
                        await chats.CloseAsync(session.ChatId, reason, ct);
                        SessionCookie.Clear(http.Response);
                        return new GuardResult.Denied(Problems.SessionEnded(reason), SessionEnd.Code(reason));
                    default:
                        return new GuardResult.Denied(Problems.BankUnavailable(), "bank_unavailable");
                }

            default:
                throw new InvalidOperationException("Unknown session lookup.");
        }
    }
}

/// <summary>The opaque session cookie: HttpOnly so scripts cannot read it, Secure, SameSite=Strict.</summary>
public static class SessionCookie
{
    public const string Name = "bank_chat_session";

    public static string? Read(HttpRequest request) => request.Cookies[Name];

    // Browsers accept Secure cookies on http://localhost, so development needs no exception.
    public static void Write(HttpResponse response, string value) => response.Cookies.Append(Name, value, Options());

    public static void Clear(HttpResponse response) => response.Cookies.Delete(Name, Options());

    private static CookieOptions Options() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true,
    };
}

/// <summary>
/// Refuses state-changing calls without the chat UI's own header. A form or link on another site cannot add a custom
/// header, and a script there needs a CORS preflight this host never allows: a second guard next to SameSite=Strict.
/// </summary>
public static class ChatClientHeader
{
    public const string Name = "X-Bank-Chat";

    public static IApplicationBuilder UseChatClientHeader(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        var request = http.Request;
        var guarded = HttpMethods.IsPost(request.Method) || HttpMethods.IsPut(request.Method) || HttpMethods.IsDelete(request.Method);
        if (guarded && (request.Path.StartsWithSegments("/auth") || request.Path.StartsWithSegments("/agent"))
            && request.Headers[Name] != "1")
        {
            await Problems.MissingClientHeader().ExecuteAsync(http);
            return;
        }

        await next(http);
    });
}

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
