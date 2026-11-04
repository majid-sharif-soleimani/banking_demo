using Bank.Agent.Host.Features.Chats;
using Bank.Agent.Host.Logic;

namespace Bank.Agent.Host.Features.Auth;

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
            case SessionLookup.Ended(_, var reason):
                // A newer login closed the chat already; this browser only learns why.
                SessionCookie.Clear(http.Response);
                return new GuardResult.Denied(Problems.SessionEnded(reason), SessionEnd.Code(reason));
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
