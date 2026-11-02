using Bank.Contracts.Events;
using Bank.Lending.Api.Data;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Bank.Lending.Api.Features.Reference;

/// <summary>customer_ref from CustomerRegistered. Idempotent. Class name = inbox key.</summary>
public sealed class CustomerRegisteredHandler(LendingDbContext db, TimeProvider time) : IEventHandler<CustomerRegistered>
{
    public async Task HandleAsync(CustomerRegistered e, CancellationToken ct)
    {
        if (!await db.Customers.AnyAsync(c => c.CustomerId == e.CustomerId, ct))
        {
            db.Customers.Add(new CustomerRef { CustomerId = e.CustomerId, Name = e.Name, CreatedAt = time.GetUtcNow(), PerformedBy = e.PerformedBy });
        }
    }
}

/// <summary>account_ref from AccountOpened: the account becomes selectable as a repayment account. Idempotent.</summary>
public sealed class AccountOpenedHandler(LendingDbContext db, TimeProvider time) : IEventHandler<AccountOpened>
{
    public async Task HandleAsync(AccountOpened e, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(a => a.AccountId == e.AccountId, ct))
        {
            db.Accounts.Add(new AccountRef
            {
                AccountId = e.AccountId, CustomerId = e.CustomerId, Currency = e.Currency, CreatedAt = time.GetUtcNow(), PerformedBy = e.PerformedBy,
            });
        }
    }
}

/// <summary>currency_ref from CurrencyActivated: the minor units of each loan's currency. Idempotent.</summary>
public sealed class CurrencyActivatedHandler(LendingDbContext db, TimeProvider time) : IEventHandler<CurrencyActivated>
{
    public async Task HandleAsync(CurrencyActivated e, CancellationToken ct)
    {
        if (!await db.Currencies.AnyAsync(c => c.Code == e.Code, ct))
        {
            db.Currencies.Add(new CurrencyRef { Code = e.Code, MinorUnits = e.MinorUnits, CreatedAt = time.GetUtcNow(), PerformedBy = e.PerformedBy });
        }
    }
}
