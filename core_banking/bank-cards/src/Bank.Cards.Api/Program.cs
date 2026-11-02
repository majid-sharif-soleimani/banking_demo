using Bank.Cards.Api.Data;
using Bank.Cards.Api.Features;
using Bank.Contracts.Events;
using Bank.Messaging;
using Bank.Messaging.BusinessDays;
using Bank.Messaging.Kafka;
using Bank.Toolkit.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("cards-db");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:cards-db is required. The AppHost sets it; otherwise set ConnectionStrings__cards-db.");
}

builder.Services.AddNpgsqlDbContext<CardsDbContext>(connectionString);
builder.Services.AddMigrationsOnStartup<CardsDbContext>();
builder.Services.AddBankMessaging(o => o.ServiceName = "cards");
builder.Services.AddKafkaTransport(builder.Configuration);
builder.Services.AddBusinessDayConsumer();
builder.Services.AddEventHandler<LoanDisbursed, LoanDisbursedHandler>();
builder.Services.AddEventHandler<CurrencyActivated, CurrencyActivatedHandler>();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.MapOpenApi();
app.MapDefaultEndpoints();

CardPayments.Map(app);

app.Run();

/// <summary>Visible to WebApplicationFactory in the integration tests.</summary>
public partial class Program;
