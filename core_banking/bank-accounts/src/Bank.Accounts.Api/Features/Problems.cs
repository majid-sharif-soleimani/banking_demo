namespace Bank.Accounts.Api.Features;

/// <summary>RFC 9457 problem responses with the machine-readable code (implementation-decisions, APIs).</summary>
public static class Problems
{
    public static IResult Of(int status, string code, string title, string? detail = null) =>
        Results.Problem(statusCode: status, title: title, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>A business rule said no: 422 with the rule's code.</summary>
    public static IResult Rule(string code, string title, string? detail = null) => Of(StatusCodes.Status422UnprocessableEntity, code, title, detail);

    public static IResult NotFound(string code, string title) => Of(StatusCodes.Status404NotFound, code, title);

    public static IResult AccountNotFound() => NotFound("account_not_found", "Account not found");
}

/// <summary>X-Customer-Id, set by the gateway (stage 7), limits a call to that customer's own data; another customer's data is 404.</summary>
public static class CustomerAccess
{
    public const string HeaderName = "X-Customer-Id";

    public static bool IsOtherCustomer(HttpRequest request, Guid ownerId) =>
        request.Headers.TryGetValue(HeaderName, out var value) && (!Guid.TryParse(value, out var caller) || caller != ownerId);
}

/// <summary>
/// Staff-only reads (workspace ADR 0006): a customer's call (X-Customer-Id) is 403 and a call without X-Performed-By is 401,
/// so every read across customers has a named actor.
/// </summary>
public static class StaffAccess
{
    public static IResult? Problem(HttpRequest request) =>
        request.Headers.ContainsKey(CustomerAccess.HeaderName)
            ? Problems.Of(StatusCodes.Status403Forbidden, "staff_only", "This search is not available to customers.")
            : string.IsNullOrWhiteSpace(request.Headers[Bank.Common.PerformedBy.HeaderName])
                ? Problems.Of(StatusCodes.Status401Unauthorized, "missing_actor", $"Send {Bank.Common.PerformedBy.HeaderName} with the staff member's id.")
                : null;
}
