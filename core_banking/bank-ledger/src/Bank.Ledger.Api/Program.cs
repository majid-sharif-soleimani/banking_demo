using Bank.Contracts.Events;
using Bank.Ledger.Api.Data;
using Bank.Ledger.Api.Features;
using Bank.Messaging;
using Bank.Messaging.BusinessDays;
using Bank.Messaging.Kafka;
using Bank.Toolkit.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("ledger-db");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:ledger-db is required. The AppHost sets it; otherwise set ConnectionStrings__ledger-db.");
}

builder.Services.AddNpgsqlDbContext<LedgerDbContext>(connectionString);
builder.Services.AddMigrationsOnStartup<LedgerDbContext>();
builder.Services.AddBankMessaging(o => o.ServiceName = "ledger");
builder.Services.AddKafkaTransport(builder.Configuration);
builder.Services.AddBusinessDayConsumer();
// Ledger posts only from these events (stage 6 file); never from PaymentCollected, CardPaymentCaptured or LoanPaidOffEarly.
builder.Services.AddEventHandler<CurrencyActivated, CurrencyActivatedHandler>();
builder.Services.AddEventHandler<MoneyDeposited, MoneyDepositedHandler>();
builder.Services.AddEventHandler<MoneyWithdrawn, MoneyWithdrawnHandler>();
builder.Services.AddEventHandler<MoneyTransferred, MoneyTransferredHandler>();
builder.Services.AddEventHandler<LoanDisbursed, LoanDisbursedHandler>();
builder.Services.AddEventHandler<LoanPaymentAllocated, LoanPaymentAllocatedHandler>();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.MapOpenApi();
app.MapDefaultEndpoints();

Reports.Map(app);

app.Run();

/// <summary>Visible to WebApplicationFactory in the integration tests.</summary>
public partial class Program;
