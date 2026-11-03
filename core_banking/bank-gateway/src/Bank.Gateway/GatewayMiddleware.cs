using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace Bank.Gateway;

/// <summary>
/// A shared API key, then sessions and correlation (workspace ADR 0007). When an API key is configured, every /api request
/// must carry it in X-Api-Key (401 otherwise); the gateway removes the header before forwarding, so no service sees the
/// key. The key proves that the caller is the trusted agent; the session token says on whose behalf it acts. Every /api
/// request outside /api/identity must carry "Authorization: Bearer &lt;token&gt;" from POST /api/identity/login. The gateway
/// asks the identity service who the token stands for (GET /sessions/current, which also restarts the idle timeout) and
/// sets the identity header itself: X-Performed-By = the username for the admin, X-Customer-Id = the customer id for a
/// customer. Identity headers sent by the caller are always dropped, so nobody can claim to be someone else, and the
/// token itself is not forwarded. A refused token gets the identity service's own 401 (session_expired,
/// session_replaced, invalid_token). The gateway keeps or adds X-Correlation-Id on the request and the response. A client's
/// correlation id is kept only if it is short and plain (letters, digits, '-', '_', '.', ':'); otherwise a new one replaces
/// it, so nothing odd reaches the services' logs. Today the id is echoed and forwarded; the services do not log it yet.
/// </summary>
public static class GatewayMiddleware
{
    public const string CustomerHeader = "X-Customer-Id";
    public const string PerformedByHeader = "X-Performed-By";
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string ApiKeyHeader = "X-Api-Key";

    /// <summary>The identity service's name behind the gateway, and the named HttpClient that reaches it.</summary>
    public const string Identity = "identity";

    private sealed record Session(string Username, string Role, Guid? CustomerId);

    /// <param name="apiKey">The expected key; blank means no key is asked for (Development only, see Program.cs).</param>
    public static IApplicationBuilder UseBankHeaders(this IApplicationBuilder app, string apiKey) => app.Use(async (context, next) =>
    {
        var request = context.Request;
        if (!request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        if (!string.IsNullOrWhiteSpace(apiKey) && !KeysMatch(request.Headers[ApiKeyHeader].ToString(), apiKey))
        {
            await Problem(StatusCodes.Status401Unauthorized, "invalid_api_key", "Missing or wrong API key.", $"Send the gateway's key in {ApiKeyHeader}.")
                .ExecuteAsync(context);
            return;
        }

        request.Headers.Remove(ApiKeyHeader);
        request.Headers.Remove(CustomerHeader);
        request.Headers.Remove(PerformedByHeader);

        // Login, logout and "who am I" go to the identity service as they are; it reads the token itself.
        if (!request.Path.StartsWithSegments($"/api/{Identity}"))
        {
            var authorization = request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                await Problem(StatusCodes.Status401Unauthorized, "missing_token", "Log in first.",
                    $"Send Authorization: Bearer <token> with the token from POST /api/{Identity}/login.").ExecuteAsync(context);
                return;
            }

            var (session, refusal) = await AskIdentityAsync(context, authorization);
            if (session is null)
            {
                await refusal!.ExecuteAsync(context);
                return;
            }

            request.Headers.Remove("Authorization");
            if (session is { Role: "customer", CustomerId: { } customerId })
            {
                request.Headers[CustomerHeader] = customerId.ToString();
            }
            else
            {
                request.Headers[PerformedByHeader] = session.Username;
            }
        }

        var given = request.Headers[CorrelationHeader].ToString();
        var correlation = IsSafeCorrelationId(given) ? given : Guid.NewGuid().ToString();
        request.Headers[CorrelationHeader] = correlation;
        context.Response.Headers[CorrelationHeader] = correlation;
        await next(context);
    });

    /// <summary>The session behind the token, or the answer to send instead: identity's own 401, or 503 when it is down.</summary>
    private static async Task<(Session? Session, IResult? Refusal)> AskIdentityAsync(HttpContext context, string authorization)
    {
        var client = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient(Identity);
        try
        {
            using var check = new HttpRequestMessage(HttpMethod.Get, "/sessions/current");
            check.Headers.TryAddWithoutValidation("Authorization", authorization);
            using var response = await client.SendAsync(check, context.RequestAborted);
            if (response.StatusCode == HttpStatusCode.OK && await response.Content.ReadFromJsonAsync<Session>(context.RequestAborted) is { } session)
            {
                return (session, null);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                var body = await response.Content.ReadAsStringAsync(context.RequestAborted);
                return (null, Results.Text(body, "application/problem+json", Encoding.UTF8, StatusCodes.Status401Unauthorized));
            }
        }
        catch (Exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            // Refused or timed out: the same answer as any other unexpected reply below.
        }

        return (null, Problem(StatusCodes.Status503ServiceUnavailable, "identity_unavailable", "The identity service could not check the token.",
            "Try again in a moment."));
    }

    private static IResult Problem(int status, string code, string title, string detail) =>
        Results.Problem(statusCode: status, title: title, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    // Compares fixed-length hashes in constant time, so the answer time tells nothing about the key or its length.
    private static bool KeysMatch(string given, string expected) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(given)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    private static bool IsSafeCorrelationId(string value) =>
        value.Length is > 0 and <= 128 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':');
}
