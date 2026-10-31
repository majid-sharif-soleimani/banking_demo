using Bank.Common;
using Bank.Messaging.BusinessDays;
using Bank.Messaging.Model;
using Bank.Toolkit.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bank.Accounts.Api.Data;

/// <summary>The Accounts schema: accounts and entries, idempotency records, local copies, plus the outbox and inbox.</summary>
public sealed class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : DbContext(options)
{
    public const string AccountNumberIndex = "ux_account_number";
    public const string IdempotencyKey = "pk_idempotency_record";

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<AccountEntry> Entries => Set<AccountEntry>();

    public DbSet<CustomerRef> Customers => Set<CustomerRef>();

    public DbSet<CurrencyRef> Currencies => Set<CurrencyRef>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<Transfer> Transfers => Set<Transfer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddBankMessaging();
        modelBuilder.AddLocalBusinessDays();

        modelBuilder.Entity<Account>(account =>
        {
            account.ToTable("account");
            account.Property(a => a.Number).HasMaxLength(9).IsFixedLength();
            account.HasIndex(a => a.Number).IsUnique().HasDatabaseName(AccountNumberIndex);
            account.HasIndex(a => a.CustomerId);
            account.Property(a => a.Type).HasConversion<string>().HasMaxLength(16);
            account.Property(a => a.Status).HasConversion<string>().HasMaxLength(16);
            account.Property(a => a.Currency).HasMaxLength(3);
            account.Property(a => a.Balance).HasColumnType("numeric");
            account.Ignore(a => a.BalanceMoney);
            account.Property(a => a.PerformedBy).HasMaxLength(PerformedBy.MaxLength);
            account.Property(a => a.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
            // Unconstrained numeric keeps the scale written (always the currency's minor units, AmountScale).
            // Money never goes below zero (no overdraft, design plan section 2), whatever the code does.
            account.ToTable(t => t.HasCheckConstraint("ck_account_balance_not_negative", "balance >= 0"));
        });

        modelBuilder.Entity<AccountEntry>(entry =>
        {
            entry.ToTable("account_entry");
            entry.Property(e => e.Sequence).ValueGeneratedOnAdd();
            entry.HasIndex(e => new { e.AccountId, e.Sequence });
            entry.Property(e => e.Amount).HasColumnType("numeric");
            entry.Property(e => e.BalanceAfter).HasColumnType("numeric");
            entry.Property(e => e.Kind).HasConversion<string>().HasMaxLength(32);
            entry.Property(e => e.Description).HasMaxLength(200);
            entry.Property(e => e.PerformedBy).HasMaxLength(PerformedBy.MaxLength);
            entry.Property(e => e.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
            entry.HasOne<Account>().WithMany().HasForeignKey(e => e.AccountId);
        });

        modelBuilder.Entity<Transfer>(transfer =>
        {
            transfer.ToTable("transfer");
            transfer.Property(t => t.Amount).HasColumnType("numeric");
            transfer.Property(t => t.ConvertedAmount).HasColumnType("numeric");
            transfer.Property(t => t.FxRate).HasColumnType("numeric");
            transfer.Property(t => t.Currency).HasMaxLength(3);
            transfer.Property(t => t.TargetCurrency).HasMaxLength(3);
            transfer.Property(t => t.FxSource).HasMaxLength(32);
            transfer.Property(t => t.Status).HasConversion<string>().HasMaxLength(16);
            transfer.Ignore(t => t.AmountMoney);
            transfer.Ignore(t => t.ConvertedMoney);
            transfer.HasIndex(t => t.FromAccountId);
            transfer.HasIndex(t => t.ToAccountId);
            transfer.Property(t => t.PerformedBy).HasMaxLength(PerformedBy.MaxLength);
            transfer.Property(t => t.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        });

        modelBuilder.Entity<CustomerRef>(customer =>
        {
            customer.ToTable("customer_ref");
            customer.HasKey(c => c.CustomerId);
            customer.Property(c => c.Name).HasMaxLength(200);
            customer.Property(c => c.PerformedBy).HasMaxLength(PerformedBy.MaxLength);
            customer.Property(c => c.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        });

        modelBuilder.Entity<CurrencyRef>(currency =>
        {
            currency.ToTable("currency_ref");
            currency.HasKey(c => c.Code);
            currency.Property(c => c.Code).HasMaxLength(3);
            currency.Property(c => c.PerformedBy).HasMaxLength(PerformedBy.MaxLength);
            currency.Property(c => c.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        });

        modelBuilder.Entity<IdempotencyRecord>(record =>
        {
            record.ToTable("idempotency_record");
            record.HasKey(r => new { r.Operation, r.Key }).HasName(IdempotencyKey);
            record.Property(r => r.Key).HasMaxLength(100);
            record.Property(r => r.Operation).HasMaxLength(100);
            record.Property(r => r.RequestHash).HasMaxLength(64);
            // text, not jsonb: jsonb reorders keys, and a replay must return the very same bytes.
            record.Property(r => r.ResponseBody).HasColumnType("text");
            record.Property(r => r.PerformedBy).HasMaxLength(PerformedBy.MaxLength);
            record.Property(r => r.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        });
    }
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the model; it never connects.</summary>
public sealed class AccountsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AccountsDbContext>
{
    public AccountsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>();
        options.UseNpgsqlWithBankConventions("Host=localhost");
        return new AccountsDbContext(options.Options);
    }
}
