using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Bank.Agent.IntegrationTests.Support;

/// <summary>
/// Plays the gateway with identity behind it (workspace ADR 0007): logins, tokens, one session per user, and the 401
/// codes. A test ends a session by hand with <see cref="Expire"/> or by logging the same user in again. Records every
/// request, so a test can check the API key and the logout call.
/// </summary>
public sealed class StubGateway : HttpMessageHandler
{
    public const string ApiKey = "test-gateway-key";

    /// <summary>A test user whose password differs from its username, so a test can look for the password itself.</summary>
    public const string TesterUsername = "TESTER-01";

    public const string TesterPassword = "s3cret-Pass-9137";

    public static readonly Guid TesterCustomerId = Guid.Parse("7a1c2a0e-0000-4000-8000-000000000001");

    private readonly ConcurrentDictionary<string, (string Username, string State)> tokens = new();
    private readonly ConcurrentDictionary<string, string> activeTokenOf = new();

    public ConcurrentQueue<(HttpMethod Method, string Path, string? ApiKey)> Requests { get; } = new();

    public bool Down { get; set; }

    /// <summary>Every token handed out, including ended ones.</summary>
    public ConcurrentQueue<string> IssuedTokens { get; } = new();

    public void Expire(string username)
    {
        if (activeTokenOf.TryGetValue(username, out var token))
        {
            tokens[token] = (username, "session_expired");
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        Requests.Enqueue((request.Method, path, request.Headers.TryGetValues("X-Api-Key", out var keys) ? keys.Single() : null));
        if (Down)
        {
            throw new HttpRequestException("Connection refused");
        }

        var bearer = request.Headers.Authorization?.Parameter;
        return path switch
        {
            "/api/identity/login" => Login(await request.Content!.ReadFromJsonAsync<JsonElement>(ct)),
            "/api/identity/sessions/current" => Current(bearer),
            "/api/identity/logout" => Logout(bearer),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private HttpResponseMessage Login(JsonElement body)
    {
        var username = body.GetProperty("username").GetString()!;
        var password = body.GetProperty("password").GetString()!;
        (string Role, Guid? CustomerId, string? Name)? user = (username, password) switch
        {
            ("admin", "admin") => ("admin", null, null),
            (TesterUsername, TesterPassword) => ("customer", TesterCustomerId, "Tess Tester"),
            ("IMP-C001", "IMP-C001") => ("customer", Guid.Parse("7a1c2a0e-0000-4000-8000-000000000002"), "Anna Andersson"),
            _ => null,
        };
        if (user is not { } found)
        {
            return Problem("invalid_credentials");
        }

        // A new token per login; the user's previous session is replaced (ADR 0007).
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        if (activeTokenOf.TryGetValue(username, out var previous))
        {
            tokens[previous] = (username, "session_replaced");
        }

        tokens[token] = (username, "valid");
        IssuedTokens.Enqueue(token);
        activeTokenOf[username] = token;
        return JsonContent(new { token, username, role = found.Role, customerId = found.CustomerId, name = found.Name, expiresAt = DateTimeOffset.UtcNow.AddMinutes(5) });
    }

    private HttpResponseMessage Current(string? token)
    {
        if (token is null)
        {
            return Problem("missing_token");
        }

        if (!tokens.TryGetValue(token, out var entry))
        {
            return Problem("invalid_token");
        }

        if (entry.State != "valid")
        {
            // The explaining answer comes once; after it the token is unknown.
            tokens.TryRemove(token, out _);
            return Problem(entry.State);
        }

        return JsonContent(new { username = entry.Username });
    }

    private HttpResponseMessage Logout(string? token)
    {
        if (token is null || !tokens.TryRemove(token, out _))
        {
            return Problem("invalid_token");
        }

        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }

    private static HttpResponseMessage Problem(string code) => new(HttpStatusCode.Unauthorized)
    {
        Content = System.Net.Http.Json.JsonContent.Create(new { code }),
    };

    private static HttpResponseMessage JsonContent(object body) => new(HttpStatusCode.OK)
    {
        Content = System.Net.Http.Json.JsonContent.Create(body),
    };
}
