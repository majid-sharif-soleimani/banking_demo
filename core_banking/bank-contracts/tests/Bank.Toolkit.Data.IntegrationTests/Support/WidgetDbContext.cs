using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bank.Toolkit.Data.IntegrationTests.Support;

public sealed class Widget
{
    public Guid Id { get; init; }

    public required string DisplayName { get; init; }
}

/// <summary>A minimal context with one migration, to test the Bank.Toolkit.Data registrations on their own.</summary>
public sealed class WidgetDbContext(DbContextOptions<WidgetDbContext> options) : DbContext(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Widget>().ToTable("widget");
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the model; it never connects.</summary>
public sealed class WidgetDbContextDesignTimeFactory : IDesignTimeDbContextFactory<WidgetDbContext>
{
    public WidgetDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WidgetDbContext>();
        options.UseNpgsqlWithBankConventions("Host=localhost");
        return new WidgetDbContext(options.Options);
    }
}
