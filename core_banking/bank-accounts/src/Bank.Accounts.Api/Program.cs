using Bank.Accounts.Api.Data;
using Bank.Accounts.Api.Features.Accounts;
using Bank.Accounts.Api.Features.Movements;
using Bank.Accounts.Api.Features.Reference;
using Bank.Common;
using Bank.Contracts.Events;
using Bank.Messaging;
using Bank.Messaging.BusinessDays;
using Bank.Messaging.Kafka;
using Bank.Toolkit.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("accounts-db");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:accounts-db is required. The AppHost sets it; otherwise set ConnectionStrings__accounts-db.");
}

builder.Services.AddNpgsqlDbContext<AccountsDbContext>(connectionString);
builder.Services.AddMigrationsOnStartup<AccountsDbContext>();
builder.Services.AddBankMessaging(o => o.ServiceName = "accounts");
builder.Services.AddKafkaTransport(builder.Configuration);
builder.Services.AddBusinessDayConsumer();
builder.Services.AddEventHandler<CustomerRegistered, CustomerRegisteredHandler>();
builder.Services.AddEventHandler<CurrencyActivated, CurrencyActivatedHandler>();
builder.Services.AddSingleton<IAccountNumberGenerator, RandomAccountNumberGenerator>();

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.MapOpenApi();
app.MapDefaultEndpoints();

OpenAccount.Map(app);
GetAccounts.Map(app);
CashMovements.Map(app);

app.Run();

/// <summary>Visible to WebApplicationFactory in the integration tests.</summary>
public partial class Program;
