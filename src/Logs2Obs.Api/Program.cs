using Logs2Obs.Adapters.Local.DependencyInjection;
using Logs2Obs.Api.Auth;
using Logs2Obs.Api.DependencyInjection;
using Logs2Obs.Api.Endpoints;
using Logs2Obs.Api.Grpc;
using Logs2Obs.Api.Middleware;
using Logs2Obs.Api.Options;
using Logs2Obs.Api.RateLimiting;
using Logs2Obs.Core.DependencyInjection;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MinRequestBodyDataRate = new MinDataRate(
        bytesPerSecond: 100,
        gracePeriod: TimeSpan.FromSeconds(10));
    options.Limits.MinResponseDataRate = new MinDataRate(
        bytesPerSecond: 100,
        gracePeriod: TimeSpan.FromSeconds(10));
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(120);
});

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
builder.Services.AddTenantRateLimiting(builder.Configuration);

builder.Services.AddHealthChecks();

builder.Services.AddGrpc();
builder.Services.AddOpenApi();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseForwardedHeaders();

app.UseSerilogRequestLogging();

app.UseExceptionHandler();
app.UseRequestTimeouts();

app.UseMiddleware<PayloadSizeMiddleware>();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ClaimsNormalizationMiddleware>();
app.UseMiddleware<TenantContextMiddleware>();

app.UseRateLimiter();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
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