using Bank.Common;
using Bank.Customers.Api.Data;
using Bank.Customers.Api.Logic;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bank.Customers.Api.Features.Customers;

/// <summary>
/// PATCH /customers/{id}: changes contact details only (email, phone, address, preferred language). Fields left out
/// stay as they are. Name, national id and date of birth cannot change through the API.
/// </summary>
public static class UpdateContactDetails
{
    public sealed record Request(string? Email, string? Phone, string? Address, string? PreferredLanguage);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPatch("/customers/{id:guid}", HandleAsync).WithName(nameof(UpdateContactDetails)).WithTags("Customers");

    private static async Task<Results<Ok<CustomerResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Guid id,
        Request request,
        HttpRequest http,
        [FromHeader(Name = PerformedBy.HeaderName)] string? performedBy,
        CustomersDbContext db,
        CancellationToken ct)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null || CustomerAccess.IsOtherCustomer(http, id))
        {
            return GetCustomer.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        CustomerRules.ValidateContact(errors, request.Email?.Trim(), request.PreferredLanguage);
        if (request.Phone is { } phone && (string.IsNullOrWhiteSpace(phone) || phone.Length > 32))
        {
            errors["phone"] = ["A value of at most 32 characters is required."];
        }

        if (request.Address is { } address && (string.IsNullOrWhiteSpace(address) || address.Length > 500))
        {
            errors["address"] = ["A value of at most 500 characters is required."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        customer.Email = request.Email?.Trim() ?? customer.Email;
        customer.Phone = request.Phone?.Trim() ?? customer.Phone;
        customer.Address = request.Address?.Trim() ?? customer.Address;
        customer.PreferredLanguage = request.PreferredLanguage ?? customer.PreferredLanguage;
        customer.PerformedBy = PerformedBy.From(performedBy);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(CustomerResponse.From(customer));
    }
}
