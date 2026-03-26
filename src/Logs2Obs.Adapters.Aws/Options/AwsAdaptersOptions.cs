namespace Logs2Obs.Adapters.Aws.Options;

public sealed class AwsAdaptersOptions
{
    public string Region { get; init; } = "us-east-1";
    public S3Options S3 { get; init; } = new();
    public SnsOptions Sns { get; init; } = new();
    public SqsOptions Sqs { get; init; } = new();
    public DynamoOptions Dynamo { get; init; } = new();
    public SchemaRegistryOptions SchemaRegistry { get; init; } = new();
    public AthenaOptions Athena { get; init; } = new();
    public OpenSearchOptions OpenSearch { get; init; } = new();
    public string ElastiCacheConnectionString { get; init; } = "";
    public string SecretsPrefix { get; init; } = "logs2obs/";
    public EventBridgeOptions EventBridge { get; init; } = new();
}

public sealed class S3Options
{
    public string BucketName { get; init; } = "logs2obs";
    public string KeyPrefix { get; init; } = "";
}

public sealed class SnsOptions
{
    public Dictionary<string, string> TopicArnMap { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SqsOptions
{
    public Dictionary<string, string> QueueUrlMap { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> DlqUrlMap { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public int MaxMessages { get; init; } = 10;
    public int WaitTimeSeconds { get; init; } = 20;
}

public sealed class DynamoOptions
{
    public string TableName { get; init; } = "logs2obs-metadata";
    public string TablePrefix { get; init; } = "logs2obs-";
}

public sealed class SchemaRegistryOptions
{
    public string TableName { get; init; } = "logs2obs-schema";
    public string TablePrefix { get; init; } = "logs2obs-";
}

public sealed class AthenaOptions
{
    public string Database { get; init; } = "logs2obs";
    public string OutputLocation { get; init; } = "s3://logs2obs-athena-results/";
    public string WorkGroup { get; init; } = "primary";
}

public sealed class OpenSearchOptions
{
    public string Endpoint { get; init; } = "http://localhost:9200";
    public string IndexPrefix { get; init; } = "logs2obs";
    public int NumberOfShards { get; init; } = 1;
    public int NumberOfReplicas { get; init; } = 1;
    public string IlmPolicyName { get; init; } = "logs2obs-hot";
    public string? Username { get; init; }
    public string? Password { get; init; }
}

public sealed class EventBridgeOptions
{
    public string ScheduleGroupName { get; init; } = "logs2obs";
    public string TargetArn { get; init; } = "";
    public string RoleArn { get; init; } = "";
}
