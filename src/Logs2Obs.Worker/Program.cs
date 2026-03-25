using Logs2Obs.Adapters.Local.DependencyInjection;
using Logs2Obs.Worker.Options;
using Logs2Obs.Worker.Parquet;
using Logs2Obs.Worker.Telemetry;
using Logs2Obs.Worker.Workers;
using OpenTelemetry.Metrics;
using Serilog;

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog((ctx, cfg) => cfg.WriteTo.Console())
    .ConfigureServices((ctx, services) =>
    {
        services.AddLocalAdapters(ctx.Configuration);
        services.Configure<WorkerOptions>(ctx.Configuration.GetSection("Worker"));
        services.AddSingleton<WorkerMetrics>();
        services.AddSingleton<IParquetWriter, ParquetWriter>();
        services.AddHostedService<StorageWriterWorker>();
        services.AddHostedService<SearchIndexerWorker>();
        services.AddOpenTelemetry()
            .WithMetrics(m => m
                .AddMeter("Logs2Obs.Worker")
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());
    })
    .Build();

await host.RunAsync();
