using Bank.Cards.Api.Logic;
using Bank.Common;
using Bank.Messaging.BusinessDays;
using Bank.Messaging.Model;
using Bank.Toolkit.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bank.Cards.Api.Data;

/// <summary>A seeded test card with fixed behaviour (implementation-decisions).</summary>
public sealed class TestCard
{
    public required string Number { get; init; }

    public CardBehavior Behavior { get; init; }

    public required string Currency { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

/// <summary>One manual loan payment attempt with a test card. The Idempotency-Key is unique: a retry returns this row.</summary>
public sealed class CardPayment
{
    public Guid Id { get; init; }

    public required string CardNumber { get; init; }

    public Guid LoanId { get; init; }

    public int? ScheduleItemNumber { get; init; }

    public decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required string Purpose { get; init; }

    public required string Status { get; init; }

    public string? Reason { get; init; }

    public required string IdempotencyKey { get; init; }

    public required string RequestHash { get; init; }

    public DateOnly BusinessDate { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

/// <summary>Cards' copy of a disbursed loan (from LoanDisbursed): a card payment can only be for a known loan in its currency.</summary>
public sealed class LoanRef
{
    public Guid LoanId { get; init; }

    public Guid CustomerId { get; init; }

    public required string Currency { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

public sealed class CurrencyRef
{
    public required string Code { get; init; }

    public int MinorUnits { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

public sealed class CardsDbContext(DbContextOptions<CardsDbContext> options) : DbContext(options)
{
    public const string IdempotencyIndex = "ux_card_payment_idempotency_key";

    public DbSet<TestCard> Cards => Set<TestCard>();
    public DbSet<CardPayment> Payments => Set<CardPayment>();
    public DbSet<LoanRef> Loans => Set<LoanRef>();
    public DbSet<CurrencyRef> Currencies => Set<CurrencyRef>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddBankMessaging();
        modelBuilder.AddLocalBusinessDays();
        var seededAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        modelBuilder.Entity<TestCard>(card =>
        {
            card.ToTable("test_card");
            card.HasKey(c => c.Number);
            card.Property(c => c.Number).HasMaxLength(16).IsFixedLength();
            card.Property(c => c.Behavior).HasConversion<string>().HasMaxLength(32);
            card.Property(c => c.Currency).HasMaxLength(3);
            Audit(card);
            card.HasData(
                new TestCard { Number = "9999000000000001", Behavior = CardBehavior.Approve, Currency = "SEK", CreatedAt = seededAt, PerformedBy = PerformedBy.System },
                new TestCard { Number = "9999000000000002", Behavior = CardBehavior.Decline, Currency = "SEK", CreatedAt = seededAt, PerformedBy = PerformedBy.System },
                new TestCard { Number = "9999000000000003", Behavior = CardBehavior.InsufficientFunds, Currency = "SEK", CreatedAt = seededAt, PerformedBy = PerformedBy.System });
        });

        modelBuilder.Entity<CardPayment>(payment =>
        {
            payment.ToTable("card_payment");
            payment.Property(p => p.CardNumber).HasMaxLength(16).IsFixedLength();
            payment.Property(p => p.Amount).HasColumnType("numeric");
            payment.Property(p => p.Currency).HasMaxLength(3);
            payment.Property(p => p.Purpose).HasMaxLength(32);
            payment.Property(p => p.Status).HasMaxLength(16);
            payment.Property(p => p.Reason).HasMaxLength(32);
            payment.Property(p => p.IdempotencyKey).HasMaxLength(100);
            payment.Property(p => p.RequestHash).HasMaxLength(64);
            payment.HasIndex(p => p.IdempotencyKey).IsUnique().HasDatabaseName(IdempotencyIndex);
            payment.HasIndex(p => p.LoanId);
            Audit(payment);
        });

        modelBuilder.Entity<LoanRef>(loan => { loan.ToTable("loan_ref"); loan.HasKey(l => l.LoanId); loan.Property(l => l.Currency).HasMaxLength(3); Audit(loan); });
        modelBuilder.Entity<CurrencyRef>(c => { c.ToTable("currency_ref"); c.HasKey(x => x.Code); c.Property(x => x.Code).HasMaxLength(3); Audit(c); });
    }

    private static void Audit<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> entity) where T : class
    {
        entity.Property<string>("PerformedBy").HasMaxLength(PerformedBy.MaxLength);
        entity.Property<uint>("Version").HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
    }
}

public sealed class CardsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CardsDbContext>
{
    public CardsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CardsDbContext>();
        options.UseNpgsqlWithBankConventions("Host=localhost");
        return new CardsDbContext(options.Options);
    }
}
