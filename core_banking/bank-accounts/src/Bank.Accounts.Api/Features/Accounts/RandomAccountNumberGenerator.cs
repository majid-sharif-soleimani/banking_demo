using System.Security.Cryptography;
using Bank.Accounts.Api.Logic;
using Bank.Common;

namespace Bank.Accounts.Api.Features.Accounts;

/// <summary>8 random digits (first 1–9) plus a Luhn check digit. Collisions are caught by the unique index and redrawn.</summary>
public sealed class RandomAccountNumberGenerator : IAccountNumberGenerator
{
    public Task<string> NextAsync(CancellationToken ct) =>
        Task.FromResult(AccountNumbers.Compose(RandomNumberGenerator.GetInt32(AccountNumbers.MinBody, AccountNumbers.MaxBodyExclusive)));
}
