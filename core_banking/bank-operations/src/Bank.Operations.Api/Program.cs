using Bank.Messaging;
using Bank.Messaging.Kafka;
using Bank.Operations.Api;
using Bank.Operations.Api.Data;
using Bank.Contracts.Events;
using Bank.Operations.Api.Features.BusinessDays;
using Bank.Operations.Api.Features.Currencies;
using Bank.Toolkit.Data;
using Microsoft.Extensions.Options;

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
// Operations reads exactly the topics of its end-of-day participants: one list, so they cannot drift apart.
builder.Services.AddOptions<KafkaOptions>()
    .PostConfigure<IOptions<OperationsOptions>>((kafka, operations) => kafka.SubscribeTo = operations.Value.EndOfDayParticipants);
builder.Services.AddEventHandler<DayCompleted, DayCompletedHandler>();
// After the migrations (they run in IHostedLifecycleService.StartingAsync, before any StartAsync).
builder.Services.AddHostedService<OpenFirstBusinessDay>();
builder.Services.AddOpenApi();
// RFC 9457 problem details for every error response, including unhandled exceptions.
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.MapOpenApi();
app.MapDefaultEndpoints();

ListCurrencies.Map(app);
ActivateCurrency.Map(app);
GetCurrentBusinessDay.Map(app);
CloseBusinessDay.Map(app);

app.Run();

/// <summary>Visible to WebApplicationFactory in the integration tests.</summary>
public partial class Program;
