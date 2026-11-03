using Amazon.S3;
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

// Uploaded documents (design plan ADR 18): the local file system in development, S3 on AWS (Files:Provider).
var files = builder.Configuration.GetSection(FileStorageOptions.Section).Get<FileStorageOptions>() ?? new FileStorageOptions();
builder.Services.AddOptions<FileStorageOptions>().Bind(builder.Configuration.GetSection(FileStorageOptions.Section));
if (files.Provider == "S3")
{
    if (string.IsNullOrWhiteSpace(files.Bucket))
    {
        throw new InvalidOperationException("Files:Bucket is required when Files:Provider is S3 (env/customers.env on the EC2 instance).");
    }

    builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
    builder.Services.AddSingleton<IFileStorage, S3FileStorage>();
}
else if (files.Provider == "Local")
{
    builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
}
else
{
    throw new InvalidOperationException($"Files:Provider must be Local or S3, not '{files.Provider}'.");
}

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
SearchCustomers.Map(app);
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
