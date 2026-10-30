using Bank.Messaging.Model;
using Bank.Toolkit.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bank.Messaging.IntegrationTests.Support;

/// <summary>The sender's own state change, saved by the same SaveChanges as its event.</summary>
public sealed class TestItem
{
    public Guid Id { get; init; }

    public required string Name { get; init; }

    public required string PerformedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>The receiver's effect. (MessageKey, Sequence) is the business key: the same fact under a new message id is rejected.</summary>
public sealed class ReceivedTestEvent
{
    public long Id { get; init; }

    public Guid EventId { get; init; }

    public required string MessageKey { get; init; }

    public int Sequence { get; init; }

    public decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required string PerformedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Written by a second handler of the same event.</summary>
public sealed class AuditedTestEvent
{
    public Guid EventId { get; init; }

    public required string PerformedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>The DbContext of both test services, like a real service's context: its own tables plus the messaging tables.</summary>
public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public const string BusinessKeyIndex = "uq_received_test_event_business_key";

    public DbSet<TestItem> TestItems => Set<TestItem>();

    public DbSet<ReceivedTestEvent> ReceivedTestEvents => Set<ReceivedTestEvent>();

    public DbSet<AuditedTestEvent> AuditedTestEvents => Set<AuditedTestEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddBankMessaging();

        // Table names are singular, as everywhere in the bank; columns get snake_case from the naming convention.
        modelBuilder.Entity<TestItem>().ToTable("test_item");
        modelBuilder.Entity<ReceivedTestEvent>(received =>
        {
            received.ToTable("received_test_event");
            received.HasIndex(e => new { e.MessageKey, e.Sequence }).IsUnique().HasDatabaseName(BusinessKeyIndex);
        });
        modelBuilder.Entity<AuditedTestEvent>(audited =>
        {
            audited.ToTable("audited_test_event");
            audited.HasKey(e => e.EventId);
        });
    }
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the model; it never connects.</summary>
public sealed class TestDbContextDesignTimeFactory : IDesignTimeDbContextFactory<TestDbContext>
{
    public TestDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>();
        options.UseNpgsqlWithBankConventions("Host=localhost");
        return new TestDbContext(options.Options);
    }
}
