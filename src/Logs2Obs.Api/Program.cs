using Logs2Obs.Adapters.Local.DependencyInjection;
using Logs2Obs.Api.Auth;
using Logs2Obs.Api.DependencyInjection;
using Logs2Obs.Api.Endpoints;
using Logs2Obs.Api.Grpc;
using Logs2Obs.Api.Middleware;
using Logs2Obs.Api.Options;
using Logs2Obs.Api.RateLimiting;
using Logs2Obs.Core.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();

var otelOptions = builder.Configuration.GetSection("OpenTelemetry").Get<OtelOptions>() ?? new OtelOptions();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: otelOptions.ServiceName,
        serviceVersion: otelOptions.ServiceVersion))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddPrometheusExporter());

builder.Services.AddLogs2ObsCore();
builder.Services.AddLocalAdapters(builder.Configuration);
builder.Services.AddLogs2ObsApi(builder.Configuration);
builder.Services.AddTenantRateLimiting();

builder.Services.AddHealthChecks()
    .AddCheck("ready", () => HealthCheckResult.Healthy(), tags: new[] { "ready" })
    .AddCheck("live", () => HealthCheckResult.Healthy(), tags: new[] { "live" });

builder.Services.AddGrpc();
builder.Services.AddOpenApi();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseSerilogRequestLogging();

app.UseExceptionHandler();

app.UseMiddleware<PayloadSizeMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<TenantContextMiddleware>();

app.UseRateLimiter();

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapPrometheusScrapingEndpoint("/metrics");

app.MapGrpcService<LogIngestionGrpcService>();

app.MapLogsEndpoints();
app.MapQueryEndpoints();
app.MapGraphsEndpoints();
app.MapPullJobsEndpoints();
app.MapAlertsEndpoints();
app.MapAuthEndpoints();
app.MapReplayEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

Log.Information("Logs2Obs.Api starting on {Urls}", string.Join(", ", builder.Configuration["ASPNETCORE_URLS"] ?? "http://localhost:5000"));

app.Run();
