using Bank.Lending.Api.Data;
using Bank.Lending.Api.Logic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bank.Lending.Api.Features.Applications;

/// <summary>
/// GET /loan-applications?status=&amp;customerId=&amp;limit=&amp;cursor=: the review queue. Oldest first by (created_at, id), keyset
/// paging (workspace ADR 0004), staff only (ADR 0006). A GET, because its filters are only ids and statuses (ADR 0005).
/// </summary>
public static class ApplicationQueue
{
    private sealed record Key(DateTimeOffset CreatedAt, Guid Id);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/loan-applications", ListAsync)
            .WithName("ListLoanApplications").WithTags("Applications")
            .WithSummary("List loan applications, oldest first. Filter by one or more statuses and a customer. Staff only.")
            .Produces<Page<ApplicationResponse>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> ListAsync(
        [FromQuery(Name = "status")] string[]? statuses, Guid? customerId, int? limit, string? cursor,
        HttpRequest http, LendingDbContext db, CancellationToken ct)
    {
        if ((StaffAccess.Problem(http) ?? Paging.LimitProblem(limit)) is { } problem)
        {
            return problem;
        }

        var wanted = new List<ApplicationStatus>();
        foreach (var status in statuses ?? [])
        {
            if (!Enum.TryParse<ApplicationStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return Problems.Of(StatusCodes.Status400BadRequest, "invalid_status", $"'{status}' is not an application status.",
                    $"Use one of: {string.Join(", ", Enum.GetNames<ApplicationStatus>())}.");
            }

            wanted.Add(parsed);
        }

        var after = Paging.Decode<Key>(cursor);
        if (after is null && !string.IsNullOrEmpty(cursor))
        {
            return Paging.InvalidCursor();
        }

        var query = db.Applications.AsNoTracking().Include(a => a.History).AsQueryable();
        if (wanted.Count > 0)
        {
            query = query.Where(a => wanted.Contains(a.Status));
        }

        if (customerId is { } customer)
        {
            query = query.Where(a => a.CustomerId == customer);
        }

        if (after is not null)
        {
            // A row comparison, (created_at, id) > (@c, @id), which the queue index serves directly.
            query = query.Where(a => EF.Functions.GreaterThan(ValueTuple.Create(a.CreatedAt, a.Id), ValueTuple.Create(after.CreatedAt, after.Id)));
        }

        var take = limit ?? Paging.DefaultLimit;
        var rows = await query.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).Take(take + 1).ToListAsync(ct);
        return Results.Ok(Paging.ToPage(rows, take, ApplicationResponse.From, a => new Key(a.CreatedAt, a.Id)));
    }
}
