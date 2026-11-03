using Bank.Identity.Api.Logic;
using Bank.Identity.Api.Sessions;
using Microsoft.Extensions.Options;

namespace Bank.Identity.Api.Features;

/// <summary>What a valid token stands for. The gateway turns it into X-Performed-By (admin) or X-Customer-Id (customer).</summary>
public sealed record SessionView(string Username, string Role, Guid? CustomerId, string? Name, DateTimeOffset ExpiresAt);

public sealed record LoginResponse(string Token, string Username, string Role, Guid? CustomerId, string? Name, DateTimeOffset ExpiresAt);

/// <summary>
/// POST /login, POST /logout and GET /sessions/current (workspace ADR 0007). The caller is always the agent; the token
/// says on whose behalf it acts. A token travels as "Authorization: Bearer &lt;token&gt;".
/// </summary>
public static class SessionEndpoints
{
    public sealed record LoginRequest(string? Username, string? Password);

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/login", LoginAsync)
            .WithName("Login").WithTags("Sessions")
            .WithSummary("Log in as admin, or as a customer with the national id as username and password. Returns a new token.")
            .Produces<LoginResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapPost("/logout", Logout)
            .WithName("Logout").WithTags("Sessions")
            .WithSummary("End the session of the bearer token.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        app.MapGet("/sessions/current", Current)
            .WithName("CurrentSession").WithTags("Sessions")
            .WithSummary("Who the bearer token acts for. Restarts the idle timeout; the gateway calls it for every request.")
            .Produces<SessionView>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, ISessionStore sessions, CustomerDirectory customers,
        IOptions<IdentityOptions> options, ILogger<LoginRequest> log, CancellationToken ct)
    {
        Principal principal;
        if (Credentials.IsAdmin(request.Username, request.Password))
        {
            principal = new Principal(Credentials.AdminUsername, Roles.Admin);
        }
        else if (Credentials.CustomerNationalId(request.Username, request.Password) is { } nationalId)
        {
            CustomerFound? customer;
            try
            {
                customer = await customers.FindByNationalIdAsync(nationalId, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Refused, timed out (the resilience handler's own exception) or an odd answer: all mean "not now".
                // Only the exception type is logged; nothing about the national id.
                log.LogWarning("Customer lookup failed: {Error}", ex.GetType().Name);
                return Problems.Of(StatusCodes.Status503ServiceUnavailable, "customers_unavailable",
                    "The Customers service could not be reached. Try again in a moment.");
            }

            if (customer is not { Status: "Active" })
            {
                return InvalidCredentials();
            }

            principal = new Principal(nationalId, Roles.Customer, customer.Id, customer.Name);
        }
        else
        {
            return InvalidCredentials();
        }

        var session = sessions.Start(principal);
        return Results.Ok(new LoginResponse(session.Token, principal.Username, principal.Role, principal.CustomerId, principal.Name,
            SessionRules.ExpiresAt(session, options.Value.IdleTimeout)));
    }

    private static IResult Logout(HttpRequest http, ISessionStore sessions)
    {
        if (Bearer.Token(http) is not { } token)
        {
            return Problems.MissingToken();
        }

        var state = sessions.End(token);
        return state == SessionState.Valid ? Results.NoContent() : Problems.For(state);
    }

    private static IResult Current(HttpRequest http, ISessionStore sessions, IOptions<IdentityOptions> options)
    {
        if (Bearer.Token(http) is not { } token)
        {
            return Problems.MissingToken();
        }

        var (state, session) = sessions.Use(token);
        if (state != SessionState.Valid)
        {
            return Problems.For(state);
        }

        var p = session!.Principal;
        return Results.Ok(new SessionView(p.Username, p.Role, p.CustomerId, p.Name, SessionRules.ExpiresAt(session, options.Value.IdleTimeout)));
    }

    // The same answer for a wrong password and an unknown national id, so a caller cannot probe who is a customer.
    private static IResult InvalidCredentials() =>
        Problems.Of(StatusCodes.Status401Unauthorized, "invalid_credentials", "Wrong username or password.");
}

public static class Bearer
{
    /// <summary>The token of "Authorization: Bearer &lt;token&gt;", or null when the header is missing or has another scheme.</summary>
    public static string? Token(HttpRequest request)
    {
        var value = request.Headers.Authorization.ToString();
        const string scheme = "Bearer ";
        return value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase) && value[scheme.Length..].Trim() is { Length: > 0 } token ? token : null;
    }
}

public static class Problems
{
    public static IResult Of(int status, string code, string title, string? detail = null) =>
        Results.Problem(statusCode: status, title: title, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    public static IResult MissingToken() =>
        Of(StatusCodes.Status401Unauthorized, "missing_token", "Log in first.", "Send Authorization: Bearer <token> with the token from POST /login.");

    public static IResult For(SessionState state) => state switch
    {
        SessionState.Replaced => Of(StatusCodes.Status401Unauthorized, "session_replaced",
            "Someone logged in with your username.", "This session has ended because of a newer login. Log in again."),
        SessionState.Expired => Of(StatusCodes.Status401Unauthorized, "session_expired",
            "The session expired.", "No request arrived within the idle timeout. Log in again."),
        _ => Of(StatusCodes.Status401Unauthorized, "invalid_token", "Unknown token.", "Log in again."),
    };
}
