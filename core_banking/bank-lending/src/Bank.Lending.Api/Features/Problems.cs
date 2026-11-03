namespace Bank.Lending.Api.Features;

public static class Problems
{
    public static IResult Of(int status, string code, string title, string? detail = null) =>
        Results.Problem(statusCode: status, title: title, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    public static IResult Rule(string code, string title, string? detail = null) => Of(StatusCodes.Status422UnprocessableEntity, code, title, detail);

    public static IResult NotFound(string code, string title) => Of(StatusCodes.Status404NotFound, code, title);
}

/// <summary>
/// X-Customer-Id, set by the gateway for customer calls (stage 7). A customer's call may only touch the customer's own
/// data (another customer's is 404), and may not perform review actions, which belong to staff or the agent (403).
/// </summary>
public static class CustomerAccess
{
    public const string HeaderName = "X-Customer-Id";

    public static bool IsOtherCustomer(HttpRequest request, Guid ownerId) =>
        request.Headers.TryGetValue(HeaderName, out var value) && (!Guid.TryParse(value, out var caller) || caller != ownerId);

    public static bool IsCustomerCall(HttpRequest request) => request.Headers.ContainsKey(HeaderName);

    public static IResult StaffOnly() => Problems.Of(StatusCodes.Status403Forbidden, "staff_only", "Review actions are not available to customers.");
}

/// <summary>
/// Staff-only reads (workspace ADR 0006): a customer's call is 403 and a call without X-Performed-By is 401, so every read of
/// several customers' data has a named actor. Staff-only actions keep the <see cref="CustomerAccess"/> check alone.
/// </summary>
public static class StaffAccess
{
    public static IResult? Problem(HttpRequest request) =>
        CustomerAccess.IsCustomerCall(request)
            ? Problems.Of(StatusCodes.Status403Forbidden, "staff_only", "This list is not available to customers.")
            : string.IsNullOrWhiteSpace(request.Headers[Bank.Common.PerformedBy.HeaderName])
                ? Problems.Of(StatusCodes.Status401Unauthorized, "missing_actor", $"Send {Bank.Common.PerformedBy.HeaderName} with the staff member's id.")
                : null;
}
