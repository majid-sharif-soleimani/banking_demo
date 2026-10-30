using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Bank.Toolkit.Data;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TContext"/> (scoped) on PostgreSQL with snake_case names, and the same instance as
    /// <see cref="DbContext"/>, so shared code (outbox, inbox) works on the service's context without knowing its type.
    /// The only place that names the database provider (bank-contracts ADR 0012).
    /// </summary>
    /// <param name="connectionString">The service's own role; its schema is the search path, so the model names no schema.</param>
    public static IServiceCollection AddNpgsqlDbContext<TContext>(this IServiceCollection services, string connectionString)
        where TContext : DbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<TContext>(options => options.UseNpgsqlWithBankConventions(connectionString));
        services.TryAddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        return services;
    }

    /// <summary>
    /// The provider settings every context uses; design-time factories call it too, so generated migrations match.
    /// </summary>
    public static DbContextOptionsBuilder UseNpgsqlWithBankConventions(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();

    /// <summary>Applies the pending EF Core migrations of <typeparamref name="TContext"/> before any hosted service starts.</summary>
    public static IServiceCollection AddMigrationsOnStartup<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, MigrationStartupService<TContext>>());
        return services;
    }
}
