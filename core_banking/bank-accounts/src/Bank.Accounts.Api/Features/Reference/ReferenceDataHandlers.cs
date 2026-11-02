using Bank.Accounts.Api.Data;
using Bank.Contracts.Events;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Bank.Accounts.Api.Features.Reference;

/// <summary>Keeps customer_ref from CustomerRegistered. Idempotent. The class name is an inbox key: do not rename it.</summary>
public sealed class CustomerRegisteredHandler(AccountsDbContext db, TimeProvider time) : IEventHandler<CustomerRegistered>
{
    public async Task HandleAsync(CustomerRegistered @event, CancellationToken ct)
    {
        if (!await db.Customers.AnyAsync(c => c.CustomerId == @event.CustomerId, ct))
        {
            db.Customers.Add(new CustomerRef
            {
                CustomerId = @event.CustomerId, Name = @event.Name, CreatedAt = time.GetUtcNow(), PerformedBy = @event.PerformedBy,
            });
        }
    }
}

/// <summary>Keeps currency_ref from CurrencyActivated. Idempotent. The class name is an inbox key: do not rename it.</summary>
public sealed class CurrencyActivatedHandler(AccountsDbContext db, TimeProvider time) : IEventHandler<CurrencyActivated>
{
    public async Task HandleAsync(CurrencyActivated @event, CancellationToken ct)
    {
        if (!await db.Currencies.AnyAsync(c => c.Code == @event.Code, ct))
        {
            db.Currencies.Add(new CurrencyRef
            {
                Code = @event.Code, MinorUnits = @event.MinorUnits, CreatedAt = time.GetUtcNow(), PerformedBy = @event.PerformedBy,
            });
        }
    }
}
