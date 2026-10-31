namespace Bank.Customers.Api.Data;

/// <summary>A registered customer. No KYC in phase 1 (design plan section 2); the national id is unique.</summary>
public sealed class Customer
{
    public Guid Id { get; init; }

    public required string Name { get; init; }

    public required string NationalId { get; init; }

    public DateOnly DateOfBirth { get; init; }

    public required string Email { get; set; }

    public required string Phone { get; set; }

    public required string Address { get; set; }

    /// <summary>ISO 639-1: en (default), sv or fa (design plan ADR 24).</summary>
    public required string PreferredLanguage { get; set; }

    public CustomerStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; set; }

    /// <summary>PostgreSQL's xmin, like every table's Version.</summary>
    public uint Version { get; set; }
}

public enum CustomerStatus
{
    Active,
}
