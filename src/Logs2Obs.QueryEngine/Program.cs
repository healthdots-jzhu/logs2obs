using Logs2Obs.Adapters.Local.DependencyInjection;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Commands;
using Logs2Obs.Core.Graphs;
using Logs2Obs.Core.Query;
using Logs2Obs.QueryEngine.Alerts;
using Logs2Obs.QueryEngine.AI;
using Logs2Obs.QueryEngine.Graphs;
using Logs2Obs.QueryEngine.MatViews;
using Logs2Obs.QueryEngine.Options;
using Logs2Obs.QueryEngine.Replay;
using Logs2Obs.QueryEngine.Services;
using Logs2Obs.QueryEngine.Telemetry;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using OpenTelemetry.Metrics;
using Serilog;

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog((ctx, cfg) => cfg.WriteTo.Console())
    .ConfigureServices((ctx, services) =>
    {
        services.AddLocalAdapters(ctx.Configuration);
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ExecuteSqlQuery).Assembly));

        services.Configure<QueryEngineOptions>(ctx.Configuration.GetSection("QueryEngine"));
        services.Configure<GitHubModelsOptions>(ctx.Configuration.GetSection("LightScope:AI:GitHub"));

        services.AddSingleton<ISqlSafetyValidator, SqlSafetyValidator>();
        services.AddSingleton<QueryTierRouter>();
        services.AddSingleton<QueryEngineMetrics>();
        services.AddSingleton<QueryService>();
        services.AddSingleton<SavedQueryService>();
        services.AddSingleton<ScheduledReportService>();
        services.AddSingleton<GraphSuggestionEngine>();
        services.AddSingleton<GraphRenderService>();
        services.AddScoped<AiQueryAuditLogger>();
        services.AddSingleton<AlertEvaluationMetrics>();
        services.AddSingleton<AlertNotificationService>();
        services.AddHostedService<AlertEvaluationConsumer>();
        services.AddSingleton<MatViewRefreshService>();
        services.AddHostedService<MatViewRefreshConsumer>();
        services.AddSingleton<IReplayService, ReplayService>();
        services.AddHostedService<ReplayWorker>();
        services.AddHealthChecks();

        var aiProvider = ctx.Configuration["LightScope:AI:Provider"];
        if (string.Equals(aiProvider, "GitHubModels", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient("github-models");
            services.AddSingleton<IAiService, GitHubModelsAiService>();
        }
        else if (string.Equals(aiProvider, "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            // Registered by AddLocalAdapters
        }
        else
        {
            services.AddSingleton<IAiService, NoOpAiService>();
        }

        services.AddOpenTelemetry()
            .WithMetrics(m => m
                .AddMeter("logs2obs.queryengine", "logs2obs.alerts")
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());
    })
    .ConfigureWebHostDefaults(webBuilder =>
    {
        webBuilder.UseUrls("http://+:8081");
        webBuilder.Configure(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHealthChecks("/health/live");
                endpoints.MapHealthChecks("/health/ready");
                endpoints.MapPrometheusScrapingEndpoint("/metrics");
            });
        });
    })
    .Build();

await host.RunAsync();
