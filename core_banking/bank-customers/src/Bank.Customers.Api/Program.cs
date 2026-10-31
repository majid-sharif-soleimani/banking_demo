using Bank.Common;
using Bank.Contracts.Events;
using Bank.Customers.Api.Data;
using Bank.Customers.Api.Email;
using Bank.Customers.Api.Features.Customers;
using Bank.Customers.Api.Features.Notifications;
using Bank.Customers.Api.Features.Interactions;
using Bank.Customers.Api.Features.Appointments;
using Bank.Customers.Api.Files;
using Bank.Messaging;
using Bank.Messaging.BusinessDays;
using Bank.Messaging.Kafka;
using Bank.Toolkit.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddNpgsqlDbContext<CustomersDbContext>(Required("customers-db", "the AppHost sets it"));
builder.Services.AddMigrationsOnStartup<CustomersDbContext>();
builder.Services.AddBankMessaging(o => o.ServiceName = "customers");
builder.Services.AddKafkaTransport(builder.Configuration);
builder.Services.AddBusinessDayConsumer();
builder.Services.AddEventHandler<StandingOrderFailed, StandingOrderFailedHandler>();
builder.Services.AddEventHandler<InformationRequested, InformationRequestedHandler>();
builder.Services.AddEventHandler<LoanApplicationSubmitted, LoanApplicationSubmittedHandler>();
builder.Services.AddEventHandler<LoanApplicationStatusChanged, LoanApplicationStatusChangedHandler>();
builder.Services.AddEventHandler<PaymentFailed, PaymentFailedHandler>();
builder.Services.AddEventHandler<LoanClassificationChanged, LoanClassificationChangedHandler>();
builder.Services.AddEventHandler<LoanPaidOffEarly, LoanPaidOffEarlyHandler>();
builder.Services.AddEventHandler<CardPaymentDeclined, CardPaymentDeclinedHandler>();

// Uploaded documents (design plan ADR 18): the local file system now, S3 in stage 8.
builder.Services.AddOptions<FileStorageOptions>().Bind(builder.Configuration.GetSection(FileStorageOptions.Section));
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();

builder.Services.AddOptions<EmailOptions>().Bind(builder.Configuration.GetSection(EmailOptions.Section));
builder.Services.AddSingleton(_ => SmtpEndpoint.Parse(Required("mailpit", "the AppHost sets it from the Mailpit resource")));
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.MapOpenApi();
app.MapDefaultEndpoints();

RegisterCustomer.Map(app);
GetCustomer.Map(app);
UpdateContactDetails.Map(app);
InteractionEndpoints.Map(app);
AppointmentEndpoints.Map(app);

app.Run();

string Required(string name, string hint)
{
    var value = builder.Configuration.GetConnectionString(name);
    return string.IsNullOrWhiteSpace(value)
        ? throw new InvalidOperationException($"ConnectionStrings:{name} is required ({hint}); otherwise set ConnectionStrings__{name}.")
        : value;
}

/// <summary>Visible to WebApplicationFactory in the integration tests.</summary>
public partial class Program;
