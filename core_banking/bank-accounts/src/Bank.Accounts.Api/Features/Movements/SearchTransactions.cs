using Bank.Accounts.Api.Data;
using Bank.Accounts.Api.Logic;
using Bank.Common;
using Bank.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Bank.Accounts.Api.Features.Movements;

/// <summary>One account entry with its account's number, owner and currency.</summary>
public sealed record TransactionItem(
    Guid EntryId, Guid AccountId, string AccountNumber, Guid CustomerId, DateOnly BusinessDate, string Kind, Money Amount, Money BalanceAfter,
    string Description, Guid? Reference);

/// <summary>
/// POST /transactions/search: account entries across all accounts, the base for investigating suspicious movements.
/// Filters: business date range, amount range (size of the movement, one currency), currency, movement types, customer,
/// account. Oldest first by (business_date, sequence), keyset paging (workspace ADR 0004), POST because the filters can
/// point at a person (ADR 0005), staff only (ADR 0006). Index: bank-accounts ADR 0004.
/// </summary>
public static class SearchTransactions
{
    public sealed record Request(
        DateOnly? From, DateOnly? To, Money? MinAmount, Money? MaxAmount, string? Currency, IReadOnlyList<string>? Kinds,
        Guid? CustomerId, Guid? AccountId, int? Limit, string? Cursor);

    private sealed record Key(DateOnly BusinessDate, long Sequence);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/transactions/search", HandleAsync)
            .WithName(nameof(SearchTransactions)).WithTags("Transactions")
            .WithSummary("Search account entries across accounts by date, amount, currency, type, customer or account. Staff only; stores nothing.")
            .Produces<Page<TransactionItem>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> HandleAsync(
        Request request, HttpRequest http, AccountsDbContext db, IBusinessClock clock, CancellationToken ct)
    {
        if ((StaffAccess.Problem(http) ?? Paging.LimitProblem(request.Limit)) is { } problem)
        {
            return problem;
        }

        var filter = new TransactionFilter(request.From, request.To, request.MinAmount, request.MaxAmount, request.Currency?.Trim().ToUpperInvariant());
        if (TransactionSearch.Validate(filter, clock.Today) is { } error)
        {
            return Problems.Of(StatusCodes.Status400BadRequest, error.Code, error.Message);
        }

        var kinds = new List<EntryKind>();
        foreach (var kind in request.Kinds ?? [])
        {
            if (!Enum.TryParse<EntryKind>(kind, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return Problems.Of(StatusCodes.Status400BadRequest, "invalid_kind", $"'{kind}' is not a movement type.",
                    $"Use one of: {string.Join(", ", Enum.GetNames<EntryKind>())}.");
            }

            kinds.Add(parsed);
        }

        var after = Paging.Decode<Key>(request.Cursor);
        if (after is null && !string.IsNullOrEmpty(request.Cursor))
        {
            return Paging.InvalidCursor();
        }

        var query = db.Entries.AsNoTracking()
            .Join(db.Accounts, e => e.AccountId, a => a.Id, (e, a) => new { Entry = e, a.Number, a.CustomerId, a.Currency });
        if (filter.From is { } from)
        {
            query = query.Where(r => r.Entry.BusinessDate >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(r => r.Entry.BusinessDate <= to);
        }

        if (TransactionSearch.Currency(filter) is { } currency)
        {
            query = query.Where(r => r.Currency == currency);
        }

        if (filter.MinAmount is { } min)
        {
            query = query.Where(r => Math.Abs(r.Entry.Amount) >= min.Amount);
        }

        if (filter.MaxAmount is { } max)
        {
            query = query.Where(r => Math.Abs(r.Entry.Amount) <= max.Amount);
        }

        if (kinds.Count > 0)
        {
            query = query.Where(r => kinds.Contains(r.Entry.Kind));
        }

        if (request.CustomerId is { } customerId)
        {
            query = query.Where(r => r.CustomerId == customerId);
        }

        if (request.AccountId is { } accountId)
        {
            query = query.Where(r => r.Entry.AccountId == accountId);
        }

        if (after is not null)
        {
            query = query.Where(r => EF.Functions.GreaterThan(
                ValueTuple.Create(r.Entry.BusinessDate, r.Entry.Sequence), ValueTuple.Create(after.BusinessDate, after.Sequence)));
        }

        var take = request.Limit ?? Paging.DefaultLimit;
        var rows = await query.OrderBy(r => r.Entry.BusinessDate).ThenBy(r => r.Entry.Sequence).Take(take + 1).ToListAsync(ct);
        return Results.Ok(Paging.ToPage(rows, take,
            r => new TransactionItem(r.Entry.Id, r.Entry.AccountId, r.Number, r.CustomerId, r.Entry.BusinessDate, r.Entry.Kind.ToString(),
                new Money(r.Entry.Amount, r.Currency), new Money(r.Entry.BalanceAfter, r.Currency), r.Entry.Description, r.Entry.Reference),
            r => new Key(r.Entry.BusinessDate, r.Entry.Sequence)));
    }
}
