using Bank.Customers.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bank.Customers.Api.Features.Customers;

/// <summary>GET /customers/{id}.</summary>
public static class GetCustomer
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/customers/{id:guid}", HandleAsync).WithName(nameof(GetCustomer)).WithTags("Customers");

    private static async Task<Results<Ok<CustomerResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, HttpRequest http, CustomersDbContext db, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        return customer is null || CustomerAccess.IsOtherCustomer(http, id)
            ? NotFound()
            : TypedResults.Ok(CustomerResponse.From(customer));
    }

    internal static ProblemHttpResult NotFound() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Customer not found",
            extensions: new Dictionary<string, object?> { ["code"] = "customer_not_found" });
}
