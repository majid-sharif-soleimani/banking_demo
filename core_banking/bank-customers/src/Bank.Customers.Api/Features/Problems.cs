using Bank.Common;

namespace Bank.Customers.Api.Features;

public static class Problems
{
    public static IResult Of(int status, string code, string title, string? detail = null) =>
        Results.Problem(statusCode: status, title: title, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });
}

/// <summary>
/// Staff-only reads (workspace ADR 0006): a customer's call (X-Customer-Id) is 403 and a call without X-Performed-By is 401,
/// so every read of several customers' data has a named actor.
/// </summary>
public static class StaffAccess
{
    public static IResult? Problem(HttpRequest request) =>
        request.Headers.ContainsKey(Customers.CustomerAccess.HeaderName)
            ? Problems.Of(StatusCodes.Status403Forbidden, "staff_only", "This search is not available to customers.")
            : string.IsNullOrWhiteSpace(request.Headers[PerformedBy.HeaderName])
                ? Problems.Of(StatusCodes.Status401Unauthorized, "missing_actor", $"Send {PerformedBy.HeaderName} with the staff member's id.")
                : null;
}
