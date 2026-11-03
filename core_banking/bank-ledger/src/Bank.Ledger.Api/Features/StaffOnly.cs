using Microsoft.AspNetCore.Http.HttpResults;

namespace Bank.Ledger.Api.Features;

/// <summary>
/// The journal and the trial balance show the whole bank's books: staff only. The gateway sets X-Customer-Id for every customer
/// caller (workspace ADR 0007), so its presence means "a customer": such a call is refused with 403, like the staff-only
/// actions in Lending. Found in phase 2 stage 4: before this, a customer's token could read every journal entry.
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
