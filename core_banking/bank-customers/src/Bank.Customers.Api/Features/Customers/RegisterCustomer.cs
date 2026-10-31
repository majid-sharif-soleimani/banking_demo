using Bank.Common;
using Bank.Contracts.Events;
using Bank.Customers.Api.Data;
using Bank.Customers.Api.Logic;
using Bank.Messaging;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bank.Customers.Api.Features.Customers;

/// <summary>POST /customers: registers a customer and publishes CustomerRegistered for Accounts and Lending.</summary>
public static class RegisterCustomer
{
    public sealed record Request(
        string Name, string NationalId, DateOnly DateOfBirth, string Email, string Phone, string Address, string? PreferredLanguage);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/customers", HandleAsync).WithName(nameof(RegisterCustomer)).WithTags("Customers");

    private static async Task<Results<Created<CustomerResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Request request,
        [FromHeader(Name = PerformedBy.HeaderName)] string? performedBy,
        CustomersDbContext db,
        IEventPublisher publisher,
        IBusinessClock clock,
        TimeProvider time,
        CancellationToken ct)
    {
        var input = new CustomerInput(request.Name?.Trim() ?? "", request.NationalId?.Trim() ?? "", request.DateOfBirth,
            request.Email?.Trim() ?? "", request.Phone?.Trim() ?? "", request.Address?.Trim() ?? "",
            CustomerRules.LanguageOrDefault(request.PreferredLanguage));
        var errors = CustomerRules.Validate(input, clock.Today);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (await db.Customers.AnyAsync(c => c.NationalId == input.NationalId, ct))
        {
            return NationalIdTaken();
        }

        var actor = PerformedBy.From(performedBy);
        var customer = new Customer
        {
            Id = Guid.CreateVersion7(),
            Name = input.Name,
            NationalId = input.NationalId,
            DateOfBirth = input.DateOfBirth,
            Email = input.Email,
            Phone = input.Phone,
            Address = input.Address,
            PreferredLanguage = input.PreferredLanguage,
            Status = CustomerStatus.Active,
            CreatedAt = time.GetUtcNow(),
            PerformedBy = actor,
        };
        db.Customers.Add(customer);
        await publisher.PublishAsync(
            new CustomerRegistered(Guid.NewGuid(), clock.Today, actor, customer.Id, customer.Name, customer.PreferredLanguage),
            customer.Id.ToString(), ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two registrations with the same national id at the same moment: the unique index lets one through.
            return NationalIdTaken();
        }

        return TypedResults.Created($"/customers/{customer.Id}", CustomerResponse.From(customer));
    }

    private static ProblemHttpResult NationalIdTaken() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "The national id is already registered",
            extensions: new Dictionary<string, object?> { ["code"] = "national_id_taken" });
}
