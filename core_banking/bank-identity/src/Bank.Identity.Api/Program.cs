using Bank.Identity.Api.Features;
using Bank.Identity.Api.Sessions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOptions<IdentityOptions>()
    .Bind(builder.Configuration.GetSection(IdentityOptions.Section))
    .Validate(o => o.IdleTimeout > TimeSpan.Zero, "Identity:IdleTimeout must be positive, such as \"00:05:00\".")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();
// "customers" is the Aspire resource name; service discovery (services:customers:http:0) gives the real address.
builder.Services.AddHttpClient<CustomerDirectory>(client =>
{
    client.BaseAddress = new Uri("http://customers");
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.MapOpenApi();
app.MapDefaultEndpoints();
SessionEndpoints.Map(app);

app.Run();

/// <summary>Visible to WebApplicationFactory in the integration tests.</summary>
public partial class Program;
