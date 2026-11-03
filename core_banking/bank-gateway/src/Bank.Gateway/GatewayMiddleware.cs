using System.Security.Cryptography;
using System.Text;

namespace Bank.Gateway;

/// <summary>
/// A shared API key, then simulated authentication and correlation (design plan ADR 13; stage 7 file). When an API key
/// is configured, every /api request must carry it in X-Api-Key (401 otherwise); the gateway removes the header before
/// forwarding, so no service sees the key. The key proves only that the caller is trusted, not who the customer is:
/// the identity headers below are still taken as given. Every /api request must also say who it is
/// for or who makes it: X-Customer-Id (a customer's call) or X-Performed-By (staff, agent, import). The gateway forwards
/// both unchanged, and keeps or adds X-Correlation-Id on the request and the response. A client's correlation id is kept
/// only if it is short and plain (letters, digits, '-', '_', '.', ':'); otherwise a new one replaces it, so nothing odd
/// reaches the services' logs. Today the id is echoed and forwarded; the services do not log it yet.
/// </summary>
public static class GatewayMiddleware
{
    public const string CustomerHeader = "X-Customer-Id";
    public const string PerformedByHeader = "X-Performed-By";
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string ApiKeyHeader = "X-Api-Key";

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
            await Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Missing or wrong API key.",
                    detail: $"Send the gateway's key in {ApiKeyHeader}.",
                    extensions: new Dictionary<string, object?> { ["code"] = "invalid_api_key" })
                .ExecuteAsync(context);
            return;
        }

        request.Headers.Remove(ApiKeyHeader);

        if (IsBlank(request.Headers[CustomerHeader]) && IsBlank(request.Headers[PerformedByHeader]))
        {
            await Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Who is calling?",
                    detail: $"Send {CustomerHeader} (a customer's call) or {PerformedByHeader} (staff, agent or import).",
                    extensions: new Dictionary<string, object?> { ["code"] = "missing_identity" })
                .ExecuteAsync(context);
            return;
        }

        if (request.Headers.TryGetValue(CustomerHeader, out var customer) && !Guid.TryParse(customer, out _))
        {
            await Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: $"{CustomerHeader} must be a customer id.",
                    extensions: new Dictionary<string, object?> { ["code"] = "invalid_customer_id" })
                .ExecuteAsync(context);
            return;
        }

        var given = request.Headers[CorrelationHeader].ToString();
        var correlation = IsSafeCorrelationId(given) ? given : Guid.NewGuid().ToString();
        request.Headers[CorrelationHeader] = correlation;
        context.Response.Headers[CorrelationHeader] = correlation;
        await next(context);
    });

    // Compares fixed-length hashes in constant time, so the answer time tells nothing about the key or its length.
    private static bool KeysMatch(string given, string expected) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(given)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    private static bool IsBlank(Microsoft.Extensions.Primitives.StringValues value) => string.IsNullOrWhiteSpace(value.ToString());

    private static bool IsSafeCorrelationId(string value) =>
        value.Length is > 0 and <= 128 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':');
}
