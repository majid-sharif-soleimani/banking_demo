using Microsoft.AspNetCore.Http.HttpResults;

namespace Bank.Operations.Api.Features;

/// <summary>
/// Changes to the bank's calendar and currencies are staff work. The gateway sets X-Customer-Id for every customer
/// caller (workspace ADR 0007), so its presence means "a customer": such a call is refused with 403, like the staff-only
/// actions in Lending. Found in phase 2 stage 4: before this, a customer's token could close the business day.
/// </summary>
public static class StaffOnly
{
    public const string CustomerHeader = "X-Customer-Id";

    public static ProblemHttpResult? Refuse(HttpRequest request) =>
        request.Headers.ContainsKey(CustomerHeader)
            ? TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "This action is for bank staff only",
                extensions: new Dictionary<string, object?> { ["code"] = "staff_only" })
            : null;
}
