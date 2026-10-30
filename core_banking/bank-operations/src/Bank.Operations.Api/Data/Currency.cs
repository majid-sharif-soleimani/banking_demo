namespace Bank.Operations.Api.Data;

/// <summary>
/// A currency the bank knows. Seeded inactive (ADR 0002); it becomes usable only when activated through the API,
/// which publishes CurrencyActivated so every service's local copy follows.
/// </summary>
public sealed class Currency
{
    /// <summary>ISO 4217 code, such as SEK; the primary key.</summary>
    public required string Code { get; init; }

    /// <summary>Digits after the decimal point, used by MoneyRounding; 2 for SEK.</summary>
    public int MinorUnits { get; init; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; set; }

    /// <summary>PostgreSQL's xmin, like every table's Version.</summary>
    public uint Version { get; set; }
}
