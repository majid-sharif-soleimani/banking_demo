// The local platform: one PostgreSQL database "bank" with one role and schema per service, Kafka in KRaft mode with
// Kafka UI, and Mailpit. Services are added here in the stages that create them, each with a reference to its own
// "<service>-db" connection string and to "kafka".

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    // The init scripts run in POSTGRES_DB, so the roles and schemas end up in "bank".
    .WithEnvironment("POSTGRES_DB", BankDatabase.Name)
    .WithInitFiles("../../db/init");

postgres.AddDatabase(BankDatabase.Name);

var serviceDatabases = new Dictionary<string, IResourceBuilder<IResourceWithConnectionString>>();

foreach (var service in BankDatabase.Services)
{
    // A generated secret by default; set Parameters:<service>-db-password in the user secrets to choose one.
    var password = builder.AddParameter(
        $"{service}-db-password",
        new GenerateParameterDefault { MinLength = 24, Special = false },
        secret: true);

    // 001-roles-and-schemas.sql reads it as <SERVICE>_DB_PASSWORD when it creates the role.
    postgres.WithEnvironment($"{service.ToUpperInvariant()}_DB_PASSWORD", password);

    var endpoint = postgres.Resource.PrimaryEndpoint;
    serviceDatabases[service] = builder.AddConnectionString(
        $"{service}-db",
        ReferenceExpression.Create(
            $"Host={endpoint.Property(EndpointProperty.Host)};Port={endpoint.Property(EndpointProperty.Port)};Database={BankDatabase.Name};Username={service};Password={password}"));
}

var kafka = builder.AddKafka("kafka")
    .WithKafkaUI();

var mailpit = builder.AddMailPit("mailpit");

// The services. Each gets only its own "<service>-db" connection string and Kafka; it waits for both, then applies
// its migrations on startup. /health tells the AppHost (and the tests) when it is ready.
builder.AddProject<Projects.Bank_Operations_Api>("operations")
    .WithReference(serviceDatabases["operations"])
    .WithReference(kafka)
    .WaitFor(postgres)
    .WaitFor(kafka)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.Bank_Customers_Api>("customers")
    .WithReference(serviceDatabases["customers"])
    .WithReference(kafka)
    .WithReference(mailpit)
    .WaitFor(postgres)
    .WaitFor(kafka)
    .WaitFor(mailpit)
    .WithHttpHealthCheck("/health");

builder.Build().Run();

internal static class BankDatabase
{
    public const string Name = "bank";

    // One login role and schema each, created by db/init/001-roles-and-schemas.sql. Keep both lists in sync.
    public static readonly string[] Services = ["operations", "customers", "accounts", "cards", "lending", "ledger"];
}
