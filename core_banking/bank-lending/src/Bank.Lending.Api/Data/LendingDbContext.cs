using Bank.Common;
using Bank.Lending.Api.Logic;
using Bank.Messaging.BusinessDays;
using Bank.Messaging.Model;
using Bank.Toolkit.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bank.Lending.Api.Data;

/// <summary>The Lending schema: products, applications, loans and schedules, payments, local copies, run markers.</summary>
public sealed class LendingDbContext(DbContextOptions<LendingDbContext> options) : DbContext(options)
{
    private static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public DbSet<LoanProduct> Products => Set<LoanProduct>();
    public DbSet<LoanApplication> Applications => Set<LoanApplication>();
    public DbSet<ApplicationStatusChange> StatusChanges => Set<ApplicationStatusChange>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<ScheduleItemRow> ScheduleItems => Set<ScheduleItemRow>();
    public DbSet<LoanPayment> Payments => Set<LoanPayment>();
    public DbSet<CustomerRef> Customers => Set<CustomerRef>();
    public DbSet<AccountRef> Accounts => Set<AccountRef>();
    public DbSet<CurrencyRef> Currencies => Set<CurrencyRef>();
    public DbSet<LoanDayRun> LoanDayRuns => Set<LoanDayRun>();
    public DbSet<EndOfDayRun> EndOfDayRuns => Set<EndOfDayRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddBankMessaging();
        modelBuilder.AddLocalBusinessDays();

        modelBuilder.Entity<LoanProduct>(product =>
        {
            product.ToTable("loan_product");
            product.HasKey(p => p.Code);
            product.Property(p => p.Code).HasMaxLength(16);
            product.Property(p => p.Name).HasMaxLength(100);
            product.Property(p => p.RepaymentType).HasConversion<string>().HasMaxLength(16);
            product.Property(p => p.Currency).HasMaxLength(3);
            foreach (var amount in new[] { nameof(LoanProduct.MinAmount), nameof(LoanProduct.MaxAmount), nameof(LoanProduct.AnnualRate),
                         nameof(LoanProduct.PenaltyDailyRate), nameof(LoanProduct.PayoffMinPercent), nameof(LoanProduct.PayoffCap) })
            {
                product.Property(amount).HasColumnType("numeric");
            }

            Audit(product);
            // implementation-decisions, "Loan products" (demo values).
            product.HasData(
                Product("CAR", "Car loan", RepaymentType.Annuity, 20_000m, 600_000m, 12, 84, 0.0695m, 0.0005m, 5_000m, null),
                Product("MORTGAGE", "Mortgage", RepaymentType.Annuity, 500_000m, 10_000_000m, 60, 360, 0.0425m, 0.0003m, 20_000m, null),
                Product("PURCHASE", "Purchase financing", RepaymentType.Staged, 10_000m, 300_000m, 3, 24, 0.085m, 0.0005m, 3_000m, 4));
        });

        modelBuilder.Entity<LoanApplication>(application =>
        {
            application.ToTable("loan_application");
            application.Property(a => a.ProductCode).HasMaxLength(16);
            application.Property(a => a.RequestedAmount).HasColumnType("numeric");
            application.Property(a => a.Currency).HasMaxLength(3);
            application.Property(a => a.ProposedStages).HasColumnType("jsonb");
            application.Property(a => a.Purpose).HasMaxLength(500);
            application.Property(a => a.Status).HasConversion<string>().HasMaxLength(32);
            application.HasIndex(a => a.CustomerId);
            application.HasMany(a => a.History).WithOne().HasForeignKey(h => h.ApplicationId);
            Audit(application);
        });

        modelBuilder.Entity<ApplicationStatusChange>(change =>
        {
            change.ToTable("loan_application_status_change");
            // Ids are set in code (Guid v7). Without this, a new row added through application.History with its id already
            // set looks like an existing row to EF, which then UPDATEs nothing instead of INSERTing.
            change.Property(c => c.Id).ValueGeneratedNever();
            change.Property(c => c.FromStatus).HasConversion<string>().HasMaxLength(32);
            change.Property(c => c.ToStatus).HasConversion<string>().HasMaxLength(32);
            change.Property(c => c.Reason).HasMaxLength(500);
            Audit(change);
        });

        modelBuilder.Entity<Loan>(loan =>
        {
            loan.ToTable("loan");
            loan.Property(l => l.ProductCode).HasMaxLength(16);
            loan.Property(l => l.Currency).HasMaxLength(3);
            foreach (var amount in new[] { nameof(Loan.Principal), nameof(Loan.AnnualRate), nameof(Loan.PenaltyDailyRate),
                         nameof(Loan.PayoffMinPercent), nameof(Loan.PayoffCap), nameof(Loan.PayoffPrincipal), nameof(Loan.PayoffPenalty),
                         nameof(Loan.PayoffInterest), nameof(Loan.PayoffWaived) })
            {
                loan.Property(amount).HasColumnType("numeric");
            }

            loan.Property(l => l.RepaymentType).HasConversion<string>().HasMaxLength(16);
            loan.Property(l => l.Status).HasConversion<string>().HasMaxLength(16);
            loan.Property(l => l.Classification).HasConversion<string>().HasMaxLength(16);
            loan.Ignore(l => l.Terms);
            loan.HasIndex(l => l.CustomerId);
            loan.HasIndex(l => l.ApplicationId).IsUnique();
            loan.HasMany(l => l.Schedule).WithOne().HasForeignKey(i => i.LoanId);
            Audit(loan);
        });

        modelBuilder.Entity<ScheduleItemRow>(item =>
        {
            item.ToTable("schedule_item");
            item.HasKey(i => new { i.LoanId, i.Number });
            foreach (var amount in new[] { nameof(ScheduleItemRow.PrincipalDue), nameof(ScheduleItemRow.InterestDue), nameof(ScheduleItemRow.PenaltyDue),
                         nameof(ScheduleItemRow.PaidPrincipal), nameof(ScheduleItemRow.PaidInterest), nameof(ScheduleItemRow.PaidPenalty) })
            {
                item.Property(amount).HasColumnType("numeric");
            }

            item.Property(i => i.Status).HasConversion<string>().HasMaxLength(16);
            item.HasIndex(i => i.PendingCollectionId).IsUnique();
            Audit(item);
        });

        modelBuilder.Entity<LoanPayment>(payment =>
        {
            payment.ToTable("loan_payment");
            payment.HasIndex(p => p.SourceId).IsUnique();
            payment.HasIndex(p => p.LoanId);
            payment.Property(p => p.Source).HasConversion<string>().HasMaxLength(16);
            foreach (var amount in new[] { nameof(LoanPayment.Amount), nameof(LoanPayment.Principal), nameof(LoanPayment.Interest),
                         nameof(LoanPayment.Penalty), nameof(LoanPayment.Unallocated) })
            {
                payment.Property(amount).HasColumnType("numeric");
            }

            Audit(payment);
        });

        modelBuilder.Entity<CustomerRef>(c => { c.ToTable("customer_ref"); c.HasKey(x => x.CustomerId); c.Property(x => x.Name).HasMaxLength(200); Audit(c); });
        modelBuilder.Entity<AccountRef>(a => { a.ToTable("account_ref"); a.HasKey(x => x.AccountId); a.Property(x => x.Currency).HasMaxLength(3); Audit(a); });
        modelBuilder.Entity<CurrencyRef>(c => { c.ToTable("currency_ref"); c.HasKey(x => x.Code); c.Property(x => x.Code).HasMaxLength(3); Audit(c); });
        modelBuilder.Entity<LoanDayRun>(r => { r.ToTable("loan_day_run"); r.HasKey(x => new { x.LoanId, x.Date }); Audit(r); });
        modelBuilder.Entity<EndOfDayRun>(r => { r.ToTable("end_of_day_run"); r.HasKey(x => x.Date); Audit(r); });
    }

    /// <summary>PerformedBy length and Version (xmin) on every table.</summary>
    private static void Audit<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> entity) where T : class
    {
        entity.Property<string>("PerformedBy").HasMaxLength(PerformedBy.MaxLength);
        entity.Property<uint>("Version").HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
    }

    private static LoanProduct Product(
        string code, string name, RepaymentType type, decimal min, decimal max, int minTerm, int maxTerm, decimal rate, decimal penalty,
        decimal cap, int? maxStages) => new()
    {
        Code = code, Name = name, RepaymentType = type, Currency = "SEK", MinAmount = min, MaxAmount = max, MinTermMonths = minTerm,
        MaxTermMonths = maxTerm, AnnualRate = rate, PenaltyDailyRate = penalty, GraceDays = 5, OverdueAfterDays = 1, DefaultAfterDays = 90,
        PayoffMinPercent = 0.01m, PayoffCap = cap, MaxStages = maxStages, CreatedAt = SeededAt, PerformedBy = PerformedBy.System,
    };
}

public sealed class LendingDbContextDesignTimeFactory : IDesignTimeDbContextFactory<LendingDbContext>
{
    public LendingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LendingDbContext>();
        options.UseNpgsqlWithBankConventions("Host=localhost");
        return new LendingDbContext(options.Options);
    }
}
