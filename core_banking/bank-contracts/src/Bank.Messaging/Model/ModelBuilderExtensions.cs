using Microsoft.EntityFrameworkCore;

namespace Bank.Messaging.Model;

public static class ModelBuilderExtensions
{
    /// <summary>
    /// Adds outbox_message and inbox_message to the service's model, so its own migrations create them in its schema
    /// (bank-contracts ADR 0012). Names are set explicitly here, because the dispatcher and the inbox use them in raw SQL.
    /// </summary>
    public static ModelBuilder AddBankMessaging(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(outbox =>
        {
            outbox.ToTable("outbox_message");
            outbox.HasKey(m => m.Id).HasName("pk_outbox_message");
            outbox.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
            outbox.Property(m => m.Position).HasColumnName("position").ValueGeneratedOnAdd();
            outbox.Property(m => m.SchemaUri).HasColumnName("schema_uri");
            outbox.Property(m => m.MessageKey).HasColumnName("message_key");
            outbox.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb");
            outbox.Property(m => m.OccurredOn).HasColumnName("occurred_on");
            outbox.Property(m => m.PerformedBy).HasColumnName("performed_by");
            outbox.Property(m => m.CreatedAt).HasColumnName("created_at");
            outbox.Property(m => m.SentAt).HasColumnName("sent_at");
            outbox.Property(m => m.Attempts).HasColumnName("attempts");
            outbox.Property(m => m.LastError).HasColumnName("last_error");
            outbox.Property(m => m.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();

            // The dispatcher only ever reads unsent rows, in order.
            outbox.HasIndex(m => m.Position).HasDatabaseName("ix_outbox_message_unsent").HasFilter("sent_at IS NULL");
        });

        modelBuilder.Entity<InboxMessage>(inbox =>
        {
            inbox.ToTable("inbox_message");
            inbox.HasKey(m => new { m.MessageId, m.HandlerName }).HasName("pk_inbox_message");
            inbox.Property(m => m.MessageId).HasColumnName("message_id");
            inbox.Property(m => m.HandlerName).HasColumnName("handler_name");
            inbox.Property(m => m.SchemaUri).HasColumnName("schema_uri");
            inbox.Property(m => m.PerformedBy).HasColumnName("performed_by");
            inbox.Property(m => m.CreatedAt).HasColumnName("created_at");
            inbox.Property(m => m.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        });

        return modelBuilder;
    }

    /// <summary>Fails with the fix in the message when the service's model lacks the messaging tables.</summary>
    internal static void EnsureBankMessaging(this DbContext context)
    {
        if (context.Model.FindEntityType(typeof(OutboxMessage)) is null)
        {
            throw new InvalidOperationException(
                $"{context.GetType().Name} has no outbox. Call modelBuilder.AddBankMessaging() in its OnModelCreating and add a migration.");
        }
    }
}
