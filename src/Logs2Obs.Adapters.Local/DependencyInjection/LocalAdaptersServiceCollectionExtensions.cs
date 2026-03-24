namespace Logs2Obs.Adapters.Local.DependencyInjection;

using Logs2Obs.Adapters.Local.AI;
using Logs2Obs.Adapters.Local.Idempotency;
using Logs2Obs.Adapters.Local.MatViews;
using Logs2Obs.Adapters.Local.MessageBus;
using Logs2Obs.Adapters.Local.MetadataStore;
using Logs2Obs.Adapters.Local.ObjectStore;
using Logs2Obs.Adapters.Local.Options;
using Logs2Obs.Adapters.Local.QueryEngine;
using Logs2Obs.Adapters.Local.SchemaRegistry;
using Logs2Obs.Adapters.Local.Search;
using Logs2Obs.Adapters.Local.Secrets;
using Logs2Obs.Adapters.Local.Scheduler;
using Logs2Obs.Core.Abstractions;
using Meilisearch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quartz;
using RabbitMQ.Client;
using StackExchange.Redis;

public static class LocalAdaptersServiceCollectionExtensions
{
    public static IServiceCollection AddLocalAdapters(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MinioOptions>(configuration.GetSection("LocalAdapters:Minio"));
        services.Configure<RabbitMqOptions>(configuration.GetSection("LocalAdapters:RabbitMq"));
        services.Configure<PostgresOptions>(configuration.GetSection("LocalAdapters:Postgres"));
        services.Configure<RedisOptions>(configuration.GetSection("LocalAdapters:Redis"));
        services.Configure<MeilisearchOptions>(configuration.GetSection("LocalAdapters:Meilisearch"));
        services.Configure<DuckDbOptions>(configuration.GetSection("LocalAdapters:DuckDb"));
        services.Configure<OllamaOptions>(configuration.GetSection("LocalAdapters:Ollama"));

        // Redis
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            return ConnectionMultiplexer.Connect(opts.ConnectionString);
        });

        // Meilisearch
        services.AddSingleton<MeilisearchClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value;
            return new MeilisearchClient(opts.Url, opts.ApiKey);
        });

        // RabbitMQ connection — GetAwaiter().GetResult() is intentional here; DI factory must be synchronous
        services.AddSingleton<IConnection>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
            var factory = new ConnectionFactory
            {
                HostName = opts.HostName,
                Port = opts.Port,
                UserName = opts.UserName,
                Password = opts.Password,
                VirtualHost = opts.VirtualHost
            };
            return factory.CreateConnectionAsync().GetAwaiter().GetResult();
        });

        // HttpClient for Ollama
        services.AddHttpClient("ollama");

        // Quartz - manually register ISchedulerFactory
        services.AddSingleton<ISchedulerFactory>(sp => new Quartz.Impl.StdSchedulerFactory());

        // Register adapters
        services.AddSingleton<IObjectStore, MinioObjectStore>();
        services.AddSingleton<IMessageBus, RabbitMqMessageBus>();
        services.AddSingleton<IMetadataStore, PostgresMetadataStore>();
        services.AddSingleton<ISchemaRegistry, PostgresSchemaRegistry>();
        services.AddSingleton<ISearchIndexer, MeilisearchIndexer>();
        services.AddSingleton<IQueryEngine, DuckDbQueryEngine>();
        services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
        services.AddSingleton<IMatViewEngine, RedisMatViewEngine>();
        services.AddSingleton<ISecretStore, LocalSecretStore>();
        services.AddSingleton<IAiService, OllamaAiService>();
        services.AddSingleton<Logs2Obs.Core.Abstractions.IScheduler, QuartzScheduler>();

        // Also register in-process bus for testing/dev
        services.AddSingleton<InProcessChannelMessageBus>();

        return services;
    }
}
