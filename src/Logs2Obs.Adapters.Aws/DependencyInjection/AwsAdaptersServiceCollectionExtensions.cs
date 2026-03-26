namespace Logs2Obs.Adapters.Aws.DependencyInjection;

using Amazon.Athena;
using Amazon.DynamoDBv2;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Glue;
using Amazon.S3;
using Amazon.Scheduler;
using Amazon.SecretsManager;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Logs2Obs.Adapters.Aws.Idempotency;
using Logs2Obs.Adapters.Aws.MessageBus;
using Logs2Obs.Adapters.Aws.MetadataStore;
using Logs2Obs.Adapters.Aws.ObjectStore;
using Logs2Obs.Adapters.Aws.Options;
using Logs2Obs.Adapters.Aws.QueryEngine;
using Logs2Obs.Adapters.Aws.SchemaRegistry;
using Logs2Obs.Adapters.Aws.Search;
using Logs2Obs.Adapters.Aws.Secrets;
using Logs2Obs.Adapters.Aws.Scheduler;
using Logs2Obs.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenSearch.Client;
using StackExchange.Redis;

public static class AwsAdaptersServiceCollectionExtensions
{
    public static IServiceCollection AddAwsAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AwsAdaptersOptions>(configuration.GetSection("Aws"));

        services.AddDefaultAWSOptions(configuration.GetAWSOptions());
        services.AddAWSService<IAmazonS3>();
        services.AddAWSService<IAmazonSimpleNotificationService>();
        services.AddAWSService<IAmazonSQS>();
        services.AddAWSService<IAmazonDynamoDB>();
        services.AddAWSService<IAmazonAthena>();
        services.AddAWSService<IAmazonSecretsManager>();
        services.AddAWSService<IAmazonScheduler>();
        services.AddAWSService<IAmazonGlue>();

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AwsAdaptersOptions>>().Value;
            return ConnectionMultiplexer.Connect(opts.ElastiCacheConnectionString);
        });

        services.AddSingleton<IOpenSearchClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AwsAdaptersOptions>>().Value.OpenSearch;
            var settings = new ConnectionSettings(new Uri(opts.Endpoint));
            if (!string.IsNullOrWhiteSpace(opts.Username) && !string.IsNullOrWhiteSpace(opts.Password))
                settings = settings.BasicAuthentication(opts.Username, opts.Password);
            return new OpenSearchClient(settings);
        });

        services.AddSingleton<AwsSnsMessageBus>();
        services.AddSingleton<AwsSqsSubscriber>();
        services.AddSingleton<IObjectStore, S3ObjectStore>();
        services.AddSingleton<IMessageBus, AwsMessageBus>();
        services.AddSingleton<IMetadataStore, DynamoMetadataStore>();
        services.AddSingleton<ISchemaRegistry, DynamoSchemaRegistry>();
        services.AddSingleton<IQueryEngine, AthenaQueryEngine>();
        services.AddSingleton<ISearchIndexer, OpenSearchIndexer>();
        services.AddSingleton<IIdempotencyStore, ElastiCacheIdempotencyStore>();
        services.AddSingleton<ISecretStore, SecretsManagerSecretStore>();
        services.AddSingleton<Logs2Obs.Core.Abstractions.IScheduler, EventBridgeScheduler>();

        return services;
    }
}
