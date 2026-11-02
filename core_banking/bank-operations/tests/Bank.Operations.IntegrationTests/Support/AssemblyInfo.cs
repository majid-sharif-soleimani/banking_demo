using Bank.Testing;

// One PostgreSQL container for the whole test run; each test creates its own role and schema.
[assembly: AssemblyFixture(typeof(PostgresFixture))]
