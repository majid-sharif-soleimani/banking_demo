using Bank.Messaging;
using Bank.Messaging.Kafka;
using Bank.Operations.Api;
using Bank.Operations.Api.Data;
using Bank.Toolkit.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Aspire sets it from the AppHost resource "operations-db"; the role "operations" sees only its own schema.
var connectionString = builder.Configuration.GetConnectionString("operations-db");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:operations-db is required. The AppHost sets it; otherwise set ConnectionStrings__operations-db " +
        "or run: dotnet user-secrets set \"ConnectionStrings:operations-db\" \"<value>\" --project src/Bank.Operations.Api");
}

builder.Services.AddNpgsqlDbContext<OperationsDbContext>(connectionString);
builder.Services.AddMigrationsOnStartup<OperationsDbContext>();
builder.Services.AddBankMessaging(o => o.ServiceName = "operations");
builder.Services.AddKafkaTransport(builder.Configuration);
OperationsOptions.AddTo(builder.Services, builder.Configuration);
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();
app.MapDefaultEndpoints();

app.Run();

/// <summary>Visible to WebApplicationFactory in the integration tests.</summary>
public partial class Program;
