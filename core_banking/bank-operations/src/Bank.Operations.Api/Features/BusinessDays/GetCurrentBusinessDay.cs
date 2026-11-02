using Bank.Operations.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bank.Operations.Api.Features.BusinessDays;

/// <summary>GET /business-days/current: the business date and where it is in the end-of-day cycle.</summary>
public static class GetCurrentBusinessDay
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/business-days/current", HandleAsync).WithName(nameof(GetCurrentBusinessDay)).WithTags("Business days");

    private static async Task<Results<Ok<BusinessDayResponse>, ProblemHttpResult>> HandleAsync(OperationsDbContext db, CancellationToken ct)
    {
        var day = await db.BusinessDays.CurrentAsync(ct);
        return day is null
            ? TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "No business day is open yet")
            : TypedResults.Ok(BusinessDayResponse.From(day));
    }
}
