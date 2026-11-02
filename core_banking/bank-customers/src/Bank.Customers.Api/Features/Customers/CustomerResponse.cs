using Bank.Customers.Api.Data;

namespace Bank.Customers.Api.Features.Customers;

public sealed record CustomerResponse(
    Guid Id, string Name, string NationalId, DateOnly DateOfBirth, string Email, string Phone, string Address,
    string PreferredLanguage, string Status)
{
    public static CustomerResponse From(Customer c) =>
        new(c.Id, c.Name, c.NationalId, c.DateOfBirth, c.Email, c.Phone, c.Address, c.PreferredLanguage, c.Status.ToString());
}

/// <summary>X-Customer-Id, set by the gateway (stage 7), limits a call to that customer's own data (implementation-decisions, APIs).</summary>
public static class CustomerAccess
{
    public const string HeaderName = "X-Customer-Id";

    /// <summary>True when the call acts for another customer; the endpoint then answers 404, not 403, so ids are not probed.</summary>
    public static bool IsOtherCustomer(HttpRequest request, Guid ownerId) =>
        request.Headers.TryGetValue(HeaderName, out var value) && (!Guid.TryParse(value, out var caller) || caller != ownerId);
}
