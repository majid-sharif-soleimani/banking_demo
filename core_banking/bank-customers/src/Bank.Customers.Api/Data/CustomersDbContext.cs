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

    public const string AppointmentSlotIndex = "ux_appointment_employee_slot";

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<Interaction> Interactions => Set<Interaction>();

    public DbSet<InformationRequest> InformationRequests => Set<InformationRequest>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<ApplicationRef> Applications => Set<ApplicationRef>();

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

        modelBuilder.Entity<Employee>(employee =>
        {
            employee.ToTable("employee");
            employee.Property(e => e.Id).HasMaxLength(4);
            employee.Property(e => e.Name).HasMaxLength(100);
            Audit(employee);
            var seededAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            employee.HasData(
                new Employee { Id = "E001", Name = "Emma Clarke", CreatedAt = seededAt, PerformedBy = PerformedBy.System },
                new Employee { Id = "E002", Name = "James Porter", CreatedAt = seededAt, PerformedBy = PerformedBy.System },
                new Employee { Id = "E003", Name = "Olivia Bennett", CreatedAt = seededAt, PerformedBy = PerformedBy.System },
                new Employee { Id = "E004", Name = "Henry Walsh", CreatedAt = seededAt, PerformedBy = PerformedBy.System },
                new Employee { Id = "E005", Name = "Sophie Turner", CreatedAt = seededAt, PerformedBy = PerformedBy.System });
        });

        modelBuilder.Entity<Interaction>(interaction =>
        {
            interaction.ToTable("interaction");
            interaction.Property(i => i.Kind).HasConversion<string>().HasMaxLength(16);
            interaction.Property(i => i.FileKey).HasMaxLength(300);
            interaction.Property(i => i.FileName).HasMaxLength(200);
            interaction.Property(i => i.ContentType).HasMaxLength(100);
            interaction.HasIndex(i => i.CustomerId);
            interaction.HasIndex(i => i.SourceEventId).IsUnique();
            Audit(interaction);
        });

        modelBuilder.Entity<InformationRequest>(request =>
        {
            request.ToTable("information_request");
            request.Property(r => r.Id).ValueGeneratedNever();
            request.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
            request.HasIndex(r => r.ApplicationId);
            Audit(request);
        });

        modelBuilder.Entity<Appointment>(appointment =>
        {
            appointment.ToTable("appointment");
            appointment.Property(a => a.EmployeeId).HasMaxLength(4);
            appointment.Property(a => a.Status).HasConversion<string>().HasMaxLength(16);
            // One scheduled appointment per employee and slot; a cancelled one frees the slot.
            appointment.HasIndex(a => new { a.EmployeeId, a.StartsAt }).IsUnique()
                .HasFilter("status = 'Scheduled'").HasDatabaseName(AppointmentSlotIndex);
            Audit(appointment);
        });

        ConfigureApplications(modelBuilder);
    }

    private static void ConfigureApplications(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ApplicationRef>(application =>
        {
            application.ToTable("application_ref");
            application.HasKey(a => a.ApplicationId);
            Audit(application);
        });

    private static void Audit<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> entity) where T : class
    {
        entity.Property<string>("PerformedBy").HasMaxLength(PerformedBy.MaxLength);
        entity.Property<uint>("Version").HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
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
