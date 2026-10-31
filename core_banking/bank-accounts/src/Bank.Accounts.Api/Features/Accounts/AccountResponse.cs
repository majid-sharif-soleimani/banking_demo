using Bank.Accounts.Api.Data;
using Bank.Contracts;

namespace Bank.Accounts.Api.Features.Accounts;

public sealed record AccountResponse(
    Guid Id, string Number, Guid CustomerId, string Type, string Currency, Money Balance, string Status, DateOnly OpenedOn)
{
    public static AccountResponse From(Account a) =>
        new(a.Id, a.Number, a.CustomerId, a.Type.ToString(), a.Currency, a.BalanceMoney, a.Status.ToString(), a.OpenedOn);
}
