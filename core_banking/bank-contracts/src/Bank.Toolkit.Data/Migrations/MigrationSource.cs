namespace Bank.Toolkit.Data.Migrations;

/// <summary>The scripts of one assembly, registered with AddSqlMigrations; all sources run in registration order.</summary>
public sealed record MigrationSource(IReadOnlyList<MigrationScript> Scripts);
