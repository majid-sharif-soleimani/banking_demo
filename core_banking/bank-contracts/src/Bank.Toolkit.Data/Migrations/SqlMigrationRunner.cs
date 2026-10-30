using Bank.Toolkit.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Bank.Toolkit.Data.Migrations;

/// <summary>
/// Applies the SQL scripts that are not yet in schema_migration (bank-contracts ADR 0008). The whole run is one transaction
/// (PostgreSQL DDL is transactional), so a failing script leaves the schema as it was. An advisory lock lets only
/// one instance of an application migrate at a time. A changed script that was already applied stops the run.
/// Scripts therefore must not use statements that cannot run in a transaction, such as CREATE INDEX CONCURRENTLY.
/// </summary>
public sealed class SqlMigrationRunner(IDbConnectionFactory connectionFactory, TimeProvider timeProvider, ILogger<SqlMigrationRunner> logger)
{
    private const string CreateJournal = """
        CREATE TABLE IF NOT EXISTS schema_migration (
            script_id    text PRIMARY KEY,
            checksum     text        NOT NULL,
            performed_by text        NOT NULL,
            created_at   timestamptz NOT NULL
        )
        """;

    private const string RecordScript = """
        INSERT INTO schema_migration (script_id, checksum, performed_by, created_at)
        VALUES (@Id, @Checksum, 'system', @CreatedAt)
        """;

    /// <summary>Applies the pending scripts in the given order and returns their ids.</summary>
    public async Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<MigrationScript> scripts, CancellationToken ct)
    {
        await using var session = new DbSession(connectionFactory);
        await session.BeginAsync(ct);
        var connection = session.Connection;
        var transaction = session.Transaction;

        // Released at commit or rollback. Keyed by schema, so applications with their own schemas do not wait for each other.
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtext(current_schema()))", transaction: transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(CreateJournal, transaction: transaction, cancellationToken: ct));

        var applied = (await connection.QueryAsync<(string Id, string Checksum)>(new CommandDefinition(
                "SELECT script_id, checksum FROM schema_migration", transaction: transaction, cancellationToken: ct)))
            .ToDictionary(row => row.Id, row => row.Checksum);

        var ran = new List<string>();
        foreach (var script in scripts)
        {
            if (applied.TryGetValue(script.Id, out var checksum))
            {
                if (checksum != script.Checksum)
                {
                    throw new InvalidOperationException(
                        $"Migration {script.Id} was changed after it was applied. Never edit an applied script; add a new one.");
                }

                continue;
            }

            await connection.ExecuteAsync(new CommandDefinition(script.Sql, transaction: transaction, cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition(
                RecordScript, new { script.Id, script.Checksum, CreatedAt = timeProvider.GetUtcNow() }, transaction, cancellationToken: ct));
            ran.Add(script.Id);
        }

        await session.CommitAsync(ct);

        foreach (var id in ran)
        {
            logger.LogInformation("Applied migration {ScriptId}.", id);
        }

        return ran;
    }
}
