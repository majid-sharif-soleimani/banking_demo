using Bank.Accounts.Api.Data;
using Bank.Accounts.Api.Features.Accounts;
using Bank.Accounts.Api.Features.Movements;
using Bank.Accounts.Api.Features.Reference;
using Bank.Accounts.Api.Features.Transfers;
using Bank.Accounts.Api.Features.StandingOrders;
using Bank.Accounts.Api.Features.EndOfDay;
using Bank.Accounts.Api.ExchangeRates;
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
// End of day: Accounts is a participant (Operations:EndOfDayParticipants lists "accounts").
builder.Services.AddEventHandler<DayClosed, AccountsEndOfDayHandler>();
builder.Services.AddSingleton<IAccountNumberGenerator, RandomAccountNumberGenerator>();

// Exchange rates (design plan ADR 20): fixed rates from configuration by default, the Riksbank API when configured.
builder.Services.AddOptions<ExchangeRateOptions>().Bind(builder.Configuration.GetSection(ExchangeRateOptions.Section))
    .Validate(o => o.Provider is "Fixed" or "Riksbank", "ExchangeRates:Provider must be Fixed or Riksbank.")
    .ValidateOnStart();
if (builder.Configuration[$"{ExchangeRateOptions.Section}:Provider"] == "Riksbank")
{
    builder.Services.AddHttpClient<IExchangeRateProvider, RiksbankExchangeRateProvider>((sp, http) =>
        http.BaseAddress = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ExchangeRateOptions>>().Value.RiksbankBaseAddress);
}
else
{
    builder.Services.AddSingleton<IExchangeRateProvider, ConfiguredExchangeRateProvider>();
}

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.MapOpenApi();
app.MapDefaultEndpoints();

OpenAccount.Map(app);
GetAccounts.Map(app);
CashMovements.Map(app);
TransferEndpoints.Map(app);
Statements.Map(app);
StandingOrderEndpoints.Map(app);

app.Run();

/// <summary>Visible to WebApplicationFactory in the integration tests.</summary>
public partial class Program;
