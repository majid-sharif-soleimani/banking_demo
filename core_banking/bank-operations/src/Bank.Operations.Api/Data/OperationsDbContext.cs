using Bank.Common;
using Bank.Messaging.Model;
using Bank.Toolkit.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bank.Operations.Api.Data;

/// <summary>The Operations schema: currencies, business days and their completions, plus the outbox and inbox.</summary>
public sealed class OperationsDbContext(DbContextOptions<OperationsDbContext> options) : DbContext(options)
{
    /// <summary>The seed's CreatedAt: fixed, so the migration is the same on every machine.</summary>
    private static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public DbSet<Currency> Currencies => Set<Currency>();

    public DbSet<BusinessDay> BusinessDays => Set<BusinessDay>();

    public DbSet<BusinessDayCompletion> BusinessDayCompletions => Set<BusinessDayCompletion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddBankMessaging();

        modelBuilder.Entity<Currency>(currency =>
        {
            currency.ToTable("currency");
            currency.HasKey(c => c.Code);
            currency.Property(c => c.Code).HasMaxLength(3);
            currency.Property(c => c.PerformedBy).HasMaxLength(PerformedBy.MaxLength);
            currency.Property(c => c.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
            // Inactive until activated through the API, so CurrencyActivated reaches every service (ADR 0002).
            currency.HasData(
                new Currency { Code = "SEK", MinorUnits = 2, IsActive = false, CreatedAt = SeededAt, PerformedBy = PerformedBy.System },
                new Currency { Code = "EUR", MinorUnits = 2, IsActive = false, CreatedAt = SeededAt, PerformedBy = PerformedBy.System },
                new Currency { Code = "USD", MinorUnits = 2, IsActive = false, CreatedAt = SeededAt, PerformedBy = PerformedBy.System });
        });

        modelBuilder.Entity<BusinessDay>(day =>
        {
            day.ToTable("business_day");
            day.HasKey(d => d.Date);
            // Stored as text ("Open"), so the table reads well in SQL and a reordered enum cannot change old rows.
            day.Property(d => d.Status).HasConversion<string>().HasMaxLength(16);
            day.Property(d => d.PerformedBy).HasMaxLength(PerformedBy.MaxLength);
            day.Property(d => d.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        });

        modelBuilder.Entity<BusinessDayCompletion>(completion =>
        {
            completion.ToTable("business_day_completion");
            completion.HasKey(c => new { c.Date, c.Service });
            completion.Property(c => c.Service).HasMaxLength(64);
            completion.Property(c => c.PerformedBy).HasMaxLength(PerformedBy.MaxLength);
            completion.Property(c => c.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        });
    }
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the model; it never connects.</summary>
public sealed class OperationsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<OperationsDbContext>
{
    public OperationsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OperationsDbContext>();
        options.UseNpgsqlWithBankConventions("Host=localhost");
        return new OperationsDbContext(options.Options);
    }
}
