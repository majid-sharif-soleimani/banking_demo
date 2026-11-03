using Bank.Toolkit.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bank.Agent.Host.Data;

/// <summary>The values of <see cref="ChatMessage.Kind"/>.</summary>
public static class MessageKind
{
    // What the agent showed (author "assistant"): the JSON of a UI component.
    public const string Text = "text";
    public const string Table = "table";
    public const string Card = "card";
    public const string Chart = "chart";
    public const string Diagram = "diagram";
    public const string Form = "form";
    public const string Confirmation = "confirmation";
    public const string DocumentRequest = "document_request";

    // What the person did outside the text box (author "user"): the JSON of the action and its outcome.
    public const string FormValues = "form_values";
    public const string ChangeResult = "change_result";
    public const string Document = "document";
}

public enum ChatStatus
{
    Open,
    Closed,
}

/// <summary>
/// One conversation of one login (phase 2 design plan, decisions A10 and A12). Every login opens a new chat; when the
/// session ends the chat is closed and kept, read-only, forever in this version.
/// </summary>
public sealed class Chat
{
    public Guid Id { get; init; }

    /// <summary>The identity username: a national id or "admin".</summary>
    public required string Username { get; init; }

    /// <summary>"customer" or "admin".</summary>
    public required string Role { get; init; }

    /// <summary>The bank's customer id; null for the admin.</summary>
    public Guid? CustomerId { get; init; }

    public ChatStatus Status { get; set; }

    public DateTimeOffset OpenedAt { get; init; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>A <see cref="Logic.SessionEnd.Code"/> value, such as "session_expired".</summary>
    public string? ClosedReason { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; set; }

    /// <summary>PostgreSQL's xmin, like every table's Version.</summary>
    public uint Version { get; set; }

    public List<ChatMessage> Messages { get; init; } = [];
}

/// <summary>One message in a chat, in order. Holds what the person typed and what the agent answered, never a secret.</summary>
public sealed class ChatMessage
{
    public Guid Id { get; init; }

    public Guid ChatId { get; init; }

    /// <summary>1, 2, 3, ... within the chat; unique per chat.</summary>
    public int Sequence { get; init; }

    /// <summary>"user" or "assistant".</summary>
    public required string Author { get; init; }

    /// <summary>
    /// What <see cref="Content"/> holds: a <see cref="MessageKind"/>. "text" is plain text; the other kinds are the JSON of
    /// a UI component the agent showed (phase 2 design plan, section 5) or of an action the person took (a form sent, a
    /// change confirmed, a document uploaded), kept so the chat can be shown again and the model remembers it.
    /// </summary>
    public string Kind { get; init; } = MessageKind.Text;

    public required string Content { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required string PerformedBy { get; init; }

    public uint Version { get; set; }
}

/// <summary>The agent host's own schema "agent" (one role and schema, like a bank service: bank-platform ADR 0001).</summary>
public sealed class AgentDbContext(DbContextOptions<AgentDbContext> options) : DbContext(options)
{
    public const int UsernameMaxLength = 64;

    public DbSet<Chat> Chats => Set<Chat>();

    public DbSet<ChatMessage> Messages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Chat>(chat =>
        {
            chat.ToTable("chat");
            chat.Property(c => c.Username).HasMaxLength(UsernameMaxLength);
            chat.Property(c => c.Role).HasMaxLength(16);
            chat.Property(c => c.Status).HasConversion<string>().HasMaxLength(16);
            chat.Property(c => c.ClosedReason).HasMaxLength(32);
            // A customer's list of old chats, and closing every open chat of a user at a new login.
            chat.HasIndex(c => new { c.Username, c.OpenedAt });
            chat.HasMany(c => c.Messages).WithOne().HasForeignKey(m => m.ChatId);
            Audit(chat);
        });

        modelBuilder.Entity<ChatMessage>(message =>
        {
            message.ToTable("chat_message");
            message.Property(m => m.Author).HasMaxLength(16);
            message.Property(m => m.Kind).HasMaxLength(32).HasDefaultValue(MessageKind.Text);
            message.HasIndex(m => new { m.ChatId, m.Sequence }).IsUnique();
            Audit(message);
        });
    }

    private static void Audit<T>(EntityTypeBuilder<T> entity) where T : class
    {
        entity.Property<string>("PerformedBy").HasMaxLength(UsernameMaxLength);
        entity.Property<uint>("Version").HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
    }
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the model; it never connects.</summary>
public sealed class AgentDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AgentDbContext>
{
    public AgentDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>();
        options.UseNpgsqlWithBankConventions("Host=localhost");
        return new AgentDbContext(options.Options);
    }
}
