using Logs2Obs.Adapters.Local.DependencyInjection;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using Logs2Obs.Puller.Connectors;
using Logs2Obs.Puller.Options;
using Logs2Obs.Puller.Scheduling;
using Logs2Obs.Puller.Services;
using Logs2Obs.Puller.Telemetry;
using OpenTelemetry.Metrics;
using Quartz;
using Serilog;

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog((ctx, cfg) => cfg.WriteTo.Console())
    .ConfigureServices((ctx, services) =>
    {
        services.AddLocalAdapters(ctx.Configuration);

        services.Configure<PullerOptions>(ctx.Configuration.GetSection("Puller"));
        var maxConcurrentJobs = ctx.Configuration.GetSection("Puller").GetValue<int>("MaxConcurrentJobs", 4);

        services.AddHttpClient();

        services.AddKeyedSingleton<IPullConnector, AwsS3PullConnector>(ConnectorType.AwsS3.ToString());
        services.AddKeyedSingleton<IPullConnector, AzureBlobPullConnector>(ConnectorType.AzureBlob.ToString());
        services.AddKeyedSingleton<IPullConnector, CloudWatchPullConnector>(ConnectorType.CloudWatch.ToString());
        services.AddKeyedSingleton<IPullConnector, HttpPullConnector>(ConnectorType.Http.ToString());

        services.AddSingleton<PullJobStateService>();
        services.AddSingleton<PullerMetrics>();

        services.AddQuartz(q =>
        {
            q.UseDefaultThreadPool(tp => tp.MaxConcurrency = maxConcurrentJobs);
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        services.AddHostedService<PullJobScheduler>();

        services.AddOpenTelemetry()
            .WithMetrics(m => m
                .AddMeter("logs2obs.puller")
                .AddRuntimeInstrumentation());
    })
    .Build();

await host.RunAsync();
