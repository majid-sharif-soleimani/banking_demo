using Bank.Testing;
using Bank.Toolkit.Data.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Bank.Toolkit.Data.IntegrationTests;

public class MigrationsOnStartupTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Starting_the_host_applies_the_pending_migrations_once()
    {
        var connectionString = await postgres.CreateServiceSchemaAsync("toolkit");

        await StartAndStopAsync(connectionString);
        await StartAndStopAsync(connectionString);

        (await ScalarAsync<bool>(connectionString, "SELECT to_regclass(current_schema() || '.widget') IS NOT NULL")).ShouldBeTrue();
        (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM \"__EFMigrationsHistory\"")).ShouldBe(1);
    }

    [Fact]
    public async Task Instances_starting_together_apply_each_migration_once()
    {
        var connectionString = await postgres.CreateServiceSchemaAsync("toolkit");

        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => StartAndStopAsync(connectionString)));

        (await ScalarAsync<long>(connectionString, "SELECT count(*) FROM \"__EFMigrationsHistory\"")).ShouldBe(1);
    }

    [Fact]
    public async Task The_context_is_also_registered_as_DbContext_with_snake_case_names()
    {
        var connectionString = await postgres.CreateServiceSchemaAsync("toolkit");
        using var host = await StartAsync(connectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<WidgetDbContext>();
        scope.ServiceProvider.GetRequiredService<DbContext>().ShouldBeSameAs(context);

        context.Widgets.Add(new Widget { Id = Guid.NewGuid(), DisplayName = "first" });
        await context.SaveChangesAsync(Ct);
        (await ScalarAsync<string>(connectionString, "SELECT display_name FROM widget")).ShouldBe("first");
        await host.StopAsync(Ct);
    }

    private static async Task<IHost> StartAsync(string connectionString)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddNpgsqlDbContext<WidgetDbContext>(connectionString);
        builder.Services.AddMigrationsOnStartup<WidgetDbContext>();
        var host = builder.Build();
        await host.StartAsync(Ct);
        return host;
    }

    private static async Task StartAndStopAsync(string connectionString)
    {
        using var host = await StartAsync(connectionString);
        await host.StopAsync(Ct);
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }
}
