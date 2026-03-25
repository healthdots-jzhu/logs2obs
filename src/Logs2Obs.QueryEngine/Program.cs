using Logs2Obs.Adapters.Local.DependencyInjection;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Commands;
using Logs2Obs.Core.Query;
using Logs2Obs.QueryEngine.Options;
using Logs2Obs.QueryEngine.Services;
using Logs2Obs.QueryEngine.Telemetry;
using MediatR;
using OpenTelemetry.Metrics;
using Serilog;

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog((ctx, cfg) => cfg.WriteTo.Console())
    .ConfigureServices((ctx, services) =>
    {
        services.AddLocalAdapters(ctx.Configuration);
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ExecuteSqlQuery).Assembly));

        services.Configure<QueryEngineOptions>(ctx.Configuration.GetSection("QueryEngine"));

        services.AddSingleton<ISqlSafetyValidator, SqlSafetyValidator>();
        services.AddSingleton<QueryTierRouter>();
        services.AddSingleton<QueryEngineMetrics>();
        services.AddSingleton<QueryService>();
        services.AddSingleton<SavedQueryService>();
        services.AddSingleton<ScheduledReportService>();

        services.AddOpenTelemetry()
            .WithMetrics(m => m
                .AddMeter("logs2obs.queryengine")
                .AddRuntimeInstrumentation());
    })
    .Build();

await host.RunAsync();
