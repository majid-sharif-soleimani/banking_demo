using Bank.Customers.Api.Data;
using Bank.Customers.Api.Logic;
using Microsoft.EntityFrameworkCore;

namespace Bank.Customers.Api.Features.Customers;

/// <summary>One search hit: enough to pick the right customer. The full record is GET /customers/{id}.</summary>
public sealed record CustomerSummary(Guid Id, string Name, string Email, string Phone, string Status);

/// <summary>
/// POST /customers/search: find customers by name, email, phone, customer number (the customer id) or national id, sorted by
/// (name, id), keyset paging (workspace ADR 0004). A POST so the personal data in the filters and in the cursor never sits
/// in a URL (ADR 0005); nothing is stored and nothing about the filters is logged. Staff only (ADR 0006). Matching rules:
/// bank-customers ADR 0001.
/// </summary>
public static class SearchCustomers
{
    public sealed record Request(string? Name, string? Email, string? Phone, Guid? CustomerNumber, int? Limit, string? Cursor, string? NationalId = null);

    private sealed record Key(string Name, Guid Id);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/customers/search", HandleAsync)
            .WithName(nameof(SearchCustomers)).WithTags("Customers")
            .WithSummary("Search customers by name (contains), email, phone (digits), customer number or national id. Staff only; stores nothing.")
            .Produces<Page<CustomerSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> HandleAsync(Request request, HttpRequest http, CustomersDbContext db, CancellationToken ct)
    {
        if ((StaffAccess.Problem(http) ?? Paging.LimitProblem(request.Limit)) is { } problem)
        {
            return problem;
        }

        var filter = new CustomerSearchFilter(request.Name, request.Email, request.Phone, request.CustomerNumber, request.NationalId);
        var errors = CustomerSearch.Validate(filter);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var after = Paging.Decode<Key>(request.Cursor);
        if (after is null && !string.IsNullOrEmpty(request.Cursor))
        {
            return Paging.InvalidCursor();
        }

        var query = db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            var pattern = CustomerSearch.ContainsPattern(filter.Name);
            query = query.Where(c => EF.Functions.ILike(c.Name, pattern, @"\"));
        }

        if (!string.IsNullOrWhiteSpace(filter.Email))
        {
            var email = filter.Email.Trim().ToLowerInvariant();
            query = query.Where(c => c.Email.ToLower() == email);
        }

        if (!string.IsNullOrWhiteSpace(filter.Phone))
        {
            // regexp_replace(phone, '[^0-9]', '', 'g') = digits: the stored phone keeps whatever format it was typed in.
            var digits = CustomerSearch.PhoneDigits(filter.Phone);
            query = query.Where(c => CustomersDbContext.RegexpReplace(c.Phone, "[^0-9]", "", "g") == digits);
        }

        if (filter.CustomerNumber is { } id)
        {
            query = query.Where(c => c.Id == id);
        }

        if (!string.IsNullOrWhiteSpace(filter.NationalId))
        {
            // Unique index ux_customer_national_id: at most one hit.
            var nationalId = filter.NationalId.Trim();
            query = query.Where(c => c.NationalId == nationalId);
        }

        if (after is not null)
        {
            query = query.Where(c => EF.Functions.GreaterThan(ValueTuple.Create(c.Name, c.Id), ValueTuple.Create(after.Name, after.Id)));
        }

        var take = request.Limit ?? Paging.DefaultLimit;
        var rows = await query.OrderBy(c => c.Name).ThenBy(c => c.Id).Take(take + 1).ToListAsync(ct);
        return Results.Ok(Paging.ToPage(rows, take,
            c => new CustomerSummary(c.Id, c.Name, c.Email, c.Phone, c.Status.ToString()), c => new Key(c.Name, c.Id)));
    }
}
