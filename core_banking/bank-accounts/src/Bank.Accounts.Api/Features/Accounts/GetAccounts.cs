using Bank.Accounts.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Bank.Accounts.Api.Features.Accounts;

/// <summary>GET /accounts?customerId= and GET /accounts/{id}.</summary>
public static class GetAccounts
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/accounts", ListAsync).WithName("ListAccounts").WithTags("Accounts");
        app.MapGet("/accounts/{id:guid}", GetAsync).WithName("GetAccount").WithTags("Accounts");
    }

    private static async Task<IResult> ListAsync(Guid customerId, HttpRequest http, AccountsDbContext db, CancellationToken ct)
    {
        if (CustomerAccess.IsOtherCustomer(http, customerId))
        {
            return Results.Ok(Array.Empty<AccountResponse>());
        }

        var accounts = await db.Accounts.AsNoTracking().Where(a => a.CustomerId == customerId).OrderBy(a => a.CreatedAt).ToListAsync(ct);
        return Results.Ok(accounts.Select(AccountResponse.From));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpRequest http, AccountsDbContext db, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
        return account is null || CustomerAccess.IsOtherCustomer(http, account.CustomerId)
            ? Problems.AccountNotFound()
            : Results.Ok(AccountResponse.From(account));
    }
}
