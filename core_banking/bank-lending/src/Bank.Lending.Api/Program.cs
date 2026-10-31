using Bank.Contracts.Events;
using Bank.Lending.Api.Data;
using Bank.Lending.Api.Features.Applications;
using Bank.Lending.Api.Features.Reference;
using Bank.Lending.Api.Features.Loans;
using Bank.Lending.Api.Features.EndOfDay;
using Bank.Lending.Api.Features.Payments;
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
// End of day: Lending is a participant (Operations:EndOfDayParticipants lists "lending").
builder.Services.AddEventHandler<DayClosed, LendingEndOfDayHandler>();
builder.Services.AddEventHandler<PaymentCollected, PaymentCollectedHandler>();
builder.Services.AddEventHandler<PaymentFailed, PaymentFailedHandler>();
builder.Services.AddEventHandler<CardPaymentCaptured, CardPaymentCapturedHandler>();

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.MapOpenApi();
app.MapDefaultEndpoints();

ApplicationEndpoints.Map(app);
Disbursement.Map(app);
LoanQueries.Map(app);

app.Run();

/// <summary>Visible to WebApplicationFactory in the integration tests.</summary>
public partial class Program;
