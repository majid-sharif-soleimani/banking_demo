using Bank.Messaging.Migrations;
using Bank.Testing;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Bank.Messaging.IntegrationTests;

public class MigrationRunnerTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly MigrationScript CreateA = new("test/0001_a.sql", "CREATE TABLE a (id int PRIMARY KEY);");
    private static readonly MigrationScript CreateB = new("test/0002_b.sql", "CREATE TABLE b (id int PRIMARY KEY);");

    [Fact]
    public async Task Pending_scripts_are_applied_once()
    {
        await using var dataSource = await NewSchemaAsync();
        var runner = NewRunner(dataSource);

        (await runner.RunAsync([CreateA], Ct)).ShouldBe(["test/0001_a.sql"]);
        (await runner.RunAsync([CreateA, CreateB], Ct)).ShouldBe(["test/0002_b.sql"]);
        (await runner.RunAsync([CreateA, CreateB], Ct)).ShouldBeEmpty();

        (await TableExistsAsync(dataSource, "a")).ShouldBeTrue();
        (await TableExistsAsync(dataSource, "b")).ShouldBeTrue();
    }

    [Fact]
    public async Task Changed_applied_script_stops_the_run()
    {
        await using var dataSource = await NewSchemaAsync();
        var runner = NewRunner(dataSource);
        await runner.RunAsync([CreateA], Ct);

        var edited = CreateA with { Sql = "CREATE TABLE a (id int PRIMARY KEY, name text);" };

        var error = await Should.ThrowAsync<InvalidOperationException>(() => runner.RunAsync([edited, CreateB], Ct));
        error.Message.ShouldContain("test/0001_a.sql");
        (await TableExistsAsync(dataSource, "b")).ShouldBeFalse();
    }

    [Fact]
    public async Task Failing_script_rolls_back_the_whole_run()
    {
        await using var dataSource = await NewSchemaAsync();
        var broken = new MigrationScript("test/0002_broken.sql", "CREATE TABLE b (id int PRIMARY KEY); SELECT no_such_function();");

        await Should.ThrowAsync<PostgresException>(() => NewRunner(dataSource).RunAsync([CreateA, broken], Ct));

        (await TableExistsAsync(dataSource, "a")).ShouldBeFalse();
        (await TableExistsAsync(dataSource, "b")).ShouldBeFalse();
        (await TableExistsAsync(dataSource, "schema_migration")).ShouldBeFalse();
    }

    [Fact]
    public async Task Concurrent_runs_apply_each_script_once()
    {
        await using var dataSource = await NewSchemaAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => NewRunner(dataSource).RunAsync([CreateA, CreateB], Ct)));

        results.SelectMany(ran => ran).ShouldBe(["test/0001_a.sql", "test/0002_b.sql"], ignoreOrder: true);
    }

    [Fact]
    public void Line_endings_do_not_change_the_checksum()
    {
        new MigrationScript("x", "SELECT 1;\r\nSELECT 2;").Checksum.ShouldBe(new MigrationScript("x", "SELECT 1;\nSELECT 2;").Checksum);
    }

    private async Task<NpgsqlDataSource> NewSchemaAsync() =>
        NpgsqlDataSource.Create(await postgres.CreateServiceSchemaAsync("migrations"));

    private static SqlMigrationRunner NewRunner(NpgsqlDataSource dataSource) =>
        new(dataSource, TimeProvider.System, NullLogger<SqlMigrationRunner>.Instance);

    private static async Task<bool> TableExistsAsync(NpgsqlDataSource dataSource, string table)
    {
        await using var connection = await dataSource.OpenConnectionAsync(Ct);
        return await connection.ExecuteScalarAsync<bool>(
            "SELECT to_regclass(current_schema() || '.' || @Table) IS NOT NULL", new { Table = table });
    }
}
