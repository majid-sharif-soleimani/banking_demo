namespace Bank.Accounts.Api.Data;

/// <summary>Accounts' copy of a customer, from CustomerRegistered. Only what Accounts needs: that the customer exists.</summary>
public sealed class CustomerRef
{
    public Guid CustomerId { get; init; }

    public required string Name { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

/// <summary>Accounts' copy of an active currency, from CurrencyActivated.</summary>
public sealed class CurrencyRef
{
    public required string Code { get; init; }

    public int MinorUnits { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

/// <summary>
/// The stored answer to a money-moving request with an Idempotency-Key (ADR 0003). Saved in the same transaction as the
/// effect, so a repeated key replays the answer and never moves money twice.
/// </summary>
public sealed class IdempotencyRecord
{
    public required string Key { get; init; }

    /// <summary>The operation the key was used for, such as "deposit:{accountId}"; a key is unique per operation.</summary>
    public required string Operation { get; init; }

    /// <summary>SHA-256 of the request body: the same key with another body is rejected.</summary>
    public required string RequestHash { get; init; }

    public int StatusCode { get; init; }

    public required string ResponseBody { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}
