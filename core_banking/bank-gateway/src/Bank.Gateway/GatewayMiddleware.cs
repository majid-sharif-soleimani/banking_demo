namespace Bank.Gateway;

/// <summary>
/// Simulated authentication and correlation (design plan ADR 13; stage 7 file). Every /api request must say who it is
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

    public static IApplicationBuilder UseBankHeaders(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var request = context.Request;
        if (!request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

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

    private static bool IsBlank(Microsoft.Extensions.Primitives.StringValues value) => string.IsNullOrWhiteSpace(value.ToString());

    private static bool IsSafeCorrelationId(string value) =>
        value.Length is > 0 and <= 128 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':');
}
