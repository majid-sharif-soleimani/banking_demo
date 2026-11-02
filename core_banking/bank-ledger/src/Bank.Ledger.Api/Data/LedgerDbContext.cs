using Bank.Common;
using Bank.Ledger.Api.Logic;
using Bank.Messaging.BusinessDays;
using Bank.Messaging.Model;
using Bank.Toolkit.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bank.Ledger.Api.Data;

/// <summary>One general ledger account in one currency (the chart is created per currency when it is activated).</summary>
public sealed class GlAccount
{
    public GlCode Code { get; init; }

    public required string Currency { get; init; }

    public required string Name { get; init; }

    public GlAccountType Type { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

/// <summary>
/// A journal entry in one currency, posted from one event. (source message id, currency) is unique: an event posts once,
/// and a cross-currency transfer posts one entry per currency.
/// </summary>
public sealed class JournalEntry
{
    public Guid Id { get; init; }

    public DateOnly BusinessDate { get; init; }

    public required string Currency { get; init; }

    public required string Description { get; init; }

    /// <summary>The event that caused it, such as MoneyDeposited.</summary>
    public required string SourceEvent { get; init; }

    public Guid SourceMessageId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }

    public List<JournalLineRow> Lines { get; init; } = [];
}

public sealed class JournalLineRow
{
    public Guid Id { get; init; }

    public Guid EntryId { get; init; }

    public GlCode GlCode { get; init; }

    public required string Currency { get; init; }

    public decimal Debit { get; init; }

    public decimal Credit { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

public sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options)
{
    public DbSet<GlAccount> Accounts => Set<GlAccount>();
    public DbSet<JournalEntry> Entries => Set<JournalEntry>();
    public DbSet<JournalLineRow> Lines => Set<JournalLineRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddBankMessaging();
        modelBuilder.AddLocalBusinessDays();

        modelBuilder.Entity<GlAccount>(account =>
        {
            account.ToTable("gl_account");
            account.HasKey(a => new { a.Code, a.Currency });
            account.Property(a => a.Code).HasConversion<int>();
            account.Property(a => a.Currency).HasMaxLength(3);
            account.Property(a => a.Name).HasMaxLength(50);
            account.Property(a => a.Type).HasConversion<string>().HasMaxLength(16);
            Audit(account);
        });

        modelBuilder.Entity<JournalEntry>(entry =>
        {
            entry.ToTable("journal_entry");
            entry.Property(e => e.Id).ValueGeneratedNever();
            entry.Property(e => e.Currency).HasMaxLength(3);
            entry.Property(e => e.Description).HasMaxLength(200);
            entry.Property(e => e.SourceEvent).HasMaxLength(64);
            entry.HasIndex(e => new { e.SourceMessageId, e.Currency }).IsUnique();
            entry.HasIndex(e => e.BusinessDate);
            entry.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.EntryId);
            Audit(entry);
        });

        modelBuilder.Entity<JournalLineRow>(line =>
        {
            line.ToTable("journal_line");
            line.Property(l => l.Id).ValueGeneratedNever();
            line.Property(l => l.GlCode).HasConversion<int>();
            line.Property(l => l.Currency).HasMaxLength(3);
            line.Property(l => l.Debit).HasColumnType("numeric");
            line.Property(l => l.Credit).HasColumnType("numeric");
            // Every line is either a debit or a credit, never both and never neither.
            line.ToTable(t => t.HasCheckConstraint("ck_journal_line_one_side", "(debit = 0) <> (credit = 0) AND debit >= 0 AND credit >= 0"));
            line.HasOne<GlAccount>().WithMany().HasForeignKey(l => new { l.GlCode, l.Currency });
            Audit(line);
        });
    }

    private static void Audit<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> entity) where T : class
    {
        entity.Property<string>("PerformedBy").HasMaxLength(PerformedBy.MaxLength);
        entity.Property<uint>("Version").HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
    }
}

public sealed class LedgerDbContextDesignTimeFactory : IDesignTimeDbContextFactory<LedgerDbContext>
{
    public LedgerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LedgerDbContext>();
        options.UseNpgsqlWithBankConventions("Host=localhost");
        return new LedgerDbContext(options.Options);
    }
}
