using System;
using Amazon.CDK;
using Logs2Obs.Cdk.Stacks;

namespace Logs2Obs.Cdk;

public static class Program
{
    public static void Main(string[] args)
    {
        var app = new App();

        var account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT");
        var region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION");
        var env = new Amazon.CDK.Environment { Account = account, Region = region };

        Tags.Of(app).Add("Project", "logs2obs");
        Tags.Of(app).Add("ManagedBy", "cdk");

        var network = new NetworkStack(app, "Logs2Obs-Network", new StackProps { Env = env });
        var storage = new StorageStack(app, "Logs2Obs-Storage", new StackProps { Env = env });
        var messaging = new MessagingStack(app, "Logs2Obs-Messaging", new StackProps { Env = env });
        var database = new DatabaseStack(app, "Logs2Obs-Database", new StackProps { Env = env });
        var search = new SearchStack(app, "Logs2Obs-Search", new StackProps { Env = env });

        var cache = new CacheStack(app, "Logs2Obs-Cache", new CacheStackProps
        {
            Env = env,
            Vpc = network.Vpc,
            EcsSecurityGroup = network.EcsSecurityGroup
        });

        _ = new AuthStack(app, "Logs2Obs-Auth", new AuthStackProps
        {
            Env = env,
            TenantsTable = database.TenantsTable
        });

        _ = new ComputeStack(app, "Logs2Obs-Compute", new ComputeStackProps
        {
            Env = env,
            Vpc = network.Vpc,
            HttpsListener = network.HttpsListener,
            EcsSecurityGroup = network.EcsSecurityGroup,
            RawBucket = storage.RawBucket,
            AthenaResultsBucket = storage.AthenaResultsBucket,
            IngestTopic = messaging.IngestTopic,
            EventsTopic = messaging.EventsTopic,
            WorkerIngestQueue = messaging.WorkerIngestQueue,
            WorkerAlertsQueue = messaging.WorkerAlertsQueue,
            WorkerMatviewQueue = messaging.WorkerMatviewQueue,
            WorkerReplayQueue = messaging.WorkerReplayQueue,
            PullerScheduleQueue = messaging.PullerScheduleQueue,
            QueryAsyncQueue = messaging.QueryAsyncQueue,
            ApiWebhookQueue = messaging.ApiWebhookQueue,
            DeadletterAuditQueue = messaging.DeadletterAuditQueue,
            TenantsTable = database.TenantsTable,
            PullJobsTable = database.PullJobsTable,
            SavedQueriesTable = database.SavedQueriesTable,
            AlertRulesTable = database.AlertRulesTable,
            ReplayJobsTable = database.ReplayJobsTable,
            QueryExecutionsTable = database.QueryExecutionsTable,
            SchemaVersionsTable = database.SchemaVersionsTable,
            MetadataTable = database.MetadataTable,
            SearchDomain = search.Domain,
            RedisEndpoint = cache.RedisEndpoint
        });

        app.Synth();
    }
}
