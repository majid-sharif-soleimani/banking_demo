using Bank.Common;
using Bank.Messaging.BusinessDays;
using Bank.Messaging.Model;
using Bank.Toolkit.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bank.Customers.Api.Data;

/// <summary>The Customers schema: customers and notifications, the local business day, plus the outbox and inbox.</summary>
public sealed class CustomersDbContext(DbContextOptions<CustomersDbContext> options) : DbContext(options)
{
    public const string NationalIdIndex = "ux_customer_national_id";

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddBankMessaging();
        modelBuilder.AddLocalBusinessDays();

        modelBuilder.Entity<Customer>(customer =>
        {
            customer.ToTable("customer");
            customer.Property(c => c.Name).HasMaxLength(200);
            customer.Property(c => c.NationalId).HasMaxLength(32);
            customer.HasIndex(c => c.NationalId).IsUnique().HasDatabaseName(NationalIdIndex);
            customer.Property(c => c.Email).HasMaxLength(254);
            customer.Property(c => c.Phone).HasMaxLength(32);
            customer.Property(c => c.Address).HasMaxLength(500);
            customer.Property(c => c.PreferredLanguage).HasMaxLength(2);
            customer.Property(c => c.Status).HasConversion<string>().HasMaxLength(16);
            customer.Property(c => c.PerformedBy).HasMaxLength(PerformedBy.MaxLength);
            customer.Property(c => c.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        });

        modelBuilder.Entity<Notification>(notification =>
        {
            notification.ToTable("notification");
            notification.Property(n => n.Channel).HasMaxLength(16);
            notification.Property(n => n.Template).HasMaxLength(64);
            notification.Property(n => n.Payload).HasColumnType("jsonb");
            notification.Property(n => n.Status).HasConversion<string>().HasMaxLength(16);
            notification.HasIndex(n => n.SourceEventId).IsUnique();
            notification.HasIndex(n => n.CustomerId);
            notification.Property(n => n.PerformedBy).HasMaxLength(PerformedBy.MaxLength);
            notification.Property(n => n.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        });
    }
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the model; it never connects.</summary>
public sealed class CustomersDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CustomersDbContext>
{
    public CustomersDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CustomersDbContext>();
        options.UseNpgsqlWithBankConventions("Host=localhost");
        return new CustomersDbContext(options.Options);
    }
}
