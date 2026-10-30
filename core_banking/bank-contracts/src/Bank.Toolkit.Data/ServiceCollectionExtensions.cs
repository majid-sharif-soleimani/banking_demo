using System.Reflection;
using Bank.Toolkit.Data.Migrations;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Bank.Toolkit.Data;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PostgreSQL <see cref="IDbConnectionFactory"/> and a scoped <see cref="DbSession"/>.
    /// The container owns the factory and disposes its connection pool when the host stops.
    /// </summary>
    public static IServiceCollection AddNpgsqlConnectionFactory(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Snake-case columns map to PascalCase properties in every Dapper query of the process.
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        services.TryAddSingleton<IDbConnectionFactory>(_ => new NpgsqlConnectionFactory(connectionString));
        services.TryAddScoped<DbSession>();
        return services;
    }

    /// <summary>
    /// Applies the scripts embedded under migrations/ in <paramref name="assembly"/> before any hosted service starts.
    /// Call it once per assembly that ships scripts; <paramref name="source"/> prefixes their ids, such as "lending".
    /// </summary>
    public static IServiceCollection AddSqlMigrations(this IServiceCollection services, Assembly assembly, string source)
    {
        services.AddSingleton(new MigrationSource(MigrationScripts.FromAssembly(assembly, source)));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<SqlMigrationRunner>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, MigrationStartupService>());
        return services;
    }
}
