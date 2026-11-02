using Bank.Accounts.Api.Data;
using Bank.Common;
using Bank.Contracts.Events;
using Bank.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bank.Accounts.Api.Features.Accounts;

/// <summary>
/// POST /accounts: opens a Current or Savings account for a known customer in an active currency, with a new 9-digit
/// number, and publishes AccountOpened. A customer whose CustomerRegistered has not arrived yet is rejected with 422
/// customer_not_found; the caller retries (ADR 0001).
/// </summary>
public static class OpenAccount
{
    private const int NumberDraws = 5;

    public sealed record Request(Guid CustomerId, string Type, string Currency);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/accounts", HandleAsync).WithName(nameof(OpenAccount)).WithTags("Accounts");

    private static async Task<IResult> HandleAsync(
        Request request,
        HttpRequest http,
        [FromHeader(Name = PerformedBy.HeaderName)] string? performedBy,
        AccountsDbContext db,
        IEventPublisher publisher,
        IAccountNumberGenerator numbers,
        IBusinessClock clock,
        TimeProvider time,
        CancellationToken ct)
    {
        if (!Enum.TryParse<AccountType>(request.Type, ignoreCase: false, out var type) || !Enum.IsDefined(type))
        {
            return Problems.Of(StatusCodes.Status400BadRequest, "invalid_account_type", "The account type must be Current or Savings.");
        }

        if (CustomerAccess.IsOtherCustomer(http, request.CustomerId) || !await db.Customers.AnyAsync(c => c.CustomerId == request.CustomerId, ct))
        {
            return Problems.Rule("customer_not_found",
                "The customer is not known to Accounts yet.", "A newly registered customer reaches Accounts within moments; try again.");
        }

        if (!await db.Currencies.AnyAsync(c => c.Code == request.Currency, ct))
        {
            return Problems.Rule("currency_not_active", $"The currency '{request.Currency}' is not active.");
        }

        var actor = PerformedBy.From(performedBy);
        for (var draw = 1; ; draw++)
        {
            var account = new Account
            {
                Id = Guid.CreateVersion7(),
                Number = await numbers.NextAsync(ct),
                CustomerId = request.CustomerId,
                Type = type,
                Currency = request.Currency,
                Balance = 0m,
                Status = AccountStatus.Open,
                OpenedOn = clock.Today,
                CreatedAt = time.GetUtcNow(),
                PerformedBy = actor,
            };
            db.Accounts.Add(account);
            await publisher.PublishAsync(
                new AccountOpened(Guid.NewGuid(), clock.Today, actor, account.Id, account.Number, account.CustomerId, type.ToString(), account.Currency),
                account.Id.ToString(), ct);

            try
            {
                await db.SaveChangesAsync(ct);
                return Results.Created($"/accounts/{account.Id}", AccountResponse.From(account));
            }
            catch (DbUpdateException ex) when (draw < NumberDraws
                && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: AccountsDbContext.AccountNumberIndex })
            {
                // The random number is taken (about 1 in 90 million per existing account): draw again.
                db.ChangeTracker.Clear();
            }
        }
    }
}
