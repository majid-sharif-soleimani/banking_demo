using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// What every bank service registers first, in Program.cs: <c>builder.AddServiceDefaults()</c> and, after
/// <c>Build()</c>, <c>app.MapDefaultEndpoints()</c>. It gives logs, traces and metrics over OTLP (the Aspire dashboard
/// locally, its standalone version on AWS), the health endpoints the AppHost and Docker wait on, service discovery and
/// resilience for outgoing HTTP calls (to external APIs such as the Riksbank, and the one call between services:
/// identity looking a national id up in Customers, workspace ADR 0007). Logs can also go to Seq (<c>Seq:ServerUrl</c>,
/// bank-platform ADR 0005).
/// </summary>
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Retries, timeouts and a circuit breaker for every HttpClient.
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;

            // A second destination next to the dashboard, only where it is configured. Seq takes OTLP over HTTP on its own
            // path; the group a service's logs belong to is the resource attribute log_group (OTEL_RESOURCE_ATTRIBUTES).
            if (builder.Configuration["Seq:ServerUrl"] is { Length: > 0 } seq)
            {
                logging.AddProcessor(new BatchLogRecordExportProcessor(new OtlpLogExporter(new OtlpExporterOptions
                {
                    Endpoint = new Uri($"{seq.TrimEnd('/')}/ingest/otlp/v1/logs"),
                    Protocol = OtlpExportProtocol.HttpProtobuf,
                })));
            }
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddAspNetCoreInstrumentation(options =>
                    // Health probes run every few seconds and would drown the real requests.
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                        && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                .AddHttpClientInstrumentation());

        // The AppHost sets the endpoint for each service; without it (unit tests, a plain dotnet run) nothing is exported.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // "live": the process answers. Readiness checks (database, Kafka) are added by each service.
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // /health: every check must pass, so the service is ready for traffic.
        app.MapHealthChecks(HealthEndpointPath);

        // /alive: only the "live" checks, so a slow database does not get a running process restarted.
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live"),
        });

        return app;
    }
}
