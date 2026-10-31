using Bank.Contracts.Events;
using Bank.Lending.Api.Data;
using Bank.Lending.Api.Features.Applications;
using Bank.Lending.Api.Features.Reference;
using Bank.Messaging;
using Bank.Messaging.BusinessDays;
using Bank.Messaging.Kafka;
using Bank.Toolkit.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("lending-db");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:lending-db is required. The AppHost sets it; otherwise set ConnectionStrings__lending-db.");
}

builder.Services.AddNpgsqlDbContext<LendingDbContext>(connectionString);
builder.Services.AddMigrationsOnStartup<LendingDbContext>();
builder.Services.AddBankMessaging(o => o.ServiceName = "lending");
builder.Services.AddKafkaTransport(builder.Configuration);
builder.Services.AddBusinessDayConsumer();
builder.Services.AddEventHandler<CustomerRegistered, CustomerRegisteredHandler>();
builder.Services.AddEventHandler<AccountOpened, AccountOpenedHandler>();
builder.Services.AddEventHandler<CurrencyActivated, CurrencyActivatedHandler>();
builder.Services.AddEventHandler<InformationReceived, InformationReceivedHandler>();
builder.Services.AddEventHandler<AppointmentScheduled, AppointmentScheduledHandler>();

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.MapOpenApi();
app.MapDefaultEndpoints();

ApplicationEndpoints.Map(app);

app.Run();

/// <summary>Visible to WebApplicationFactory in the integration tests.</summary>
public partial class Program;
