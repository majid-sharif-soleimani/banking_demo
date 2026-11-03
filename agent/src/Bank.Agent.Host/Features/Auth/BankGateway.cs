using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Bank.Agent.Host.Logic;

namespace Bank.Agent.Host.Features.Auth;

public abstract record LoginOutcome
{
    public sealed record Success(string Token, Principal Principal) : LoginOutcome
    {
        // The generated ToString would print the token into any log line or message that formats this record.
        public override string ToString() => $"Success {{ Principal = {Principal} }}";
    }

    /// <summary>Wrong username or password, or an inactive customer: identity gives one answer for all of them.</summary>
    public sealed record Refused : LoginOutcome;

    public sealed record Unavailable : LoginOutcome;
}

public abstract record SessionCheck
{
    public sealed record Valid : SessionCheck;

    public sealed record Ended(ChatCloseReason Reason) : SessionCheck;

    public sealed record Unavailable : SessionCheck;
}

/// <summary>
/// The agent host's only way into the bank: the gateway, with the shared API key (design plan section 3). In this stage
/// it talks to identity only. The bank token is passed in by the caller and never logged.
/// </summary>
public sealed class BankGateway(HttpClient http, ILogger<BankGateway> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record LoginBody(string Username, string Password);

    private sealed record LoginAnswer(string Token, string Username, string Role, Guid? CustomerId, string? Name);

    private sealed record Problem(string? Code);

    public async Task<LoginOutcome> LoginAsync(string username, string password, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("/api/identity/login", new LoginBody(username, password), Json, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new LoginOutcome.Refused();
            }

            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("Login through the gateway answered {Status}", (int)response.StatusCode);
                return new LoginOutcome.Unavailable();
            }

            var answer = await response.Content.ReadFromJsonAsync<LoginAnswer>(Json, ct)
                ?? throw new JsonException("Empty login answer.");
            var role = answer.Role == "admin" ? Role.Admin : Role.Customer;
            var name = answer.Name ?? (role == Role.Admin ? "Admin" : answer.Username);
            return new LoginOutcome.Success(answer.Token, new Principal(answer.Username, role, answer.CustomerId, name));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Only the exception type: a message could echo the request.
            log.LogWarning("Login through the gateway failed: {Error}", ex.GetType().Name);
            return new LoginOutcome.Unavailable();
        }
    }

    /// <summary>Asks identity whether the token still works. A valid answer also restarts its idle timeout.</summary>
    public async Task<SessionCheck> CheckSessionAsync(string token, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/identity/sessions/current");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                return new SessionCheck.Valid();
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                var problem = await ReadProblemAsync(response, ct);
                return new SessionCheck.Ended(SessionEnd.FromIdentityCode(problem?.Code) ?? ChatCloseReason.SessionExpired);
            }

            log.LogWarning("Session check through the gateway answered {Status}", (int)response.StatusCode);
            return new SessionCheck.Unavailable();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("Session check through the gateway failed: {Error}", ex.GetType().Name);
            return new SessionCheck.Unavailable();
        }
    }

    /// <summary>Ends the bank session. Best effort: a session identity already ended is fine, and so is no answer.</summary>
    public async Task LogoutAsync(string token, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/logout");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var _ = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("Logout through the gateway failed: {Error}", ex.GetType().Name);
        }
    }

    private static async Task<Problem?> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<Problem>(Json, ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
