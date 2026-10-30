using Bank.Testing;

// One PostgreSQL container for the whole test run; each test creates its own schemas.
[assembly: AssemblyFixture(typeof(PostgresFixture))]
