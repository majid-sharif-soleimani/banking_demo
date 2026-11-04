using Bank.Agent.Host.Features.Chats;
using Bank.Agent.Host.Logic;

namespace Bank.Agent.Host.Features.Auth;

public sealed record LoginRequest(string? Username, string? Password)
{
    // The generated ToString would print the password (and the national id, which is a customer's username).
    public override string ToString() => "LoginRequest { }";
}

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
