namespace Logs2Obs.Adapters.Aws.SchemaRegistry;

using System.Globalization;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Logs2Obs.Adapters.Aws.Options;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using Logs2Obs.Core.Resilience;
using Logs2Obs.Core.Schema;
using Microsoft.Extensions.Options;

public sealed class DynamoSchemaRegistry(
    IAmazonDynamoDB dynamoDb,
    IOptions<AwsAdaptersOptions> options) : ISchemaRegistry
{
    private const string PartitionKey = "PK";
    private const string SortKey = "SK";
    private const string FieldsAttribute = "Fields";
    private const string RegisteredAtAttribute = "RegisteredAt";
    private readonly SchemaRegistryOptions _opts = options.Value.SchemaRegistry;

    public async Task<uint> GetCurrentVersionAsync(string tenantId, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<uint>();
        return await pipeline.ExecuteAsync(async token =>
        {
            var request = new QueryRequest
            {
                TableName = ResolveTableName(),
                KeyConditionExpression = $"{PartitionKey} = :tenant",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":tenant"] = new AttributeValue { S = tenantId }
                },
                ScanIndexForward = false,
                Limit = 1
            };

            var response = await dynamoDb.QueryAsync(request, token).ConfigureAwait(false);
            if (response.Items.Count == 0)
                return 0u;

            var item = response.Items[0];
            if (!item.TryGetValue(SortKey, out var versionAttr) || versionAttr.N is null)
                return 0u;

            return uint.TryParse(versionAttr.N, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
                ? version
                : 0u;
        }, ct).ConfigureAwait(false);
    }

    public async Task<uint> RegisterSchemaAsync(string tenantId, IReadOnlyList<SchemaField> fields, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<uint>();
        return await pipeline.ExecuteAsync(async token =>
        {
            var currentVersion = await GetCurrentVersionAsync(tenantId, token).ConfigureAwait(false);
            var nextVersion = currentVersion + 1;
            var fieldsJson = JsonSerializer.Serialize(fields);

            var request = new PutItemRequest
            {
                TableName = ResolveTableName(),
                Item = new Dictionary<string, AttributeValue>
                {
                    [PartitionKey] = new AttributeValue { S = tenantId },
                    [SortKey] = new AttributeValue { N = nextVersion.ToString(CultureInfo.InvariantCulture) },
                    [FieldsAttribute] = new AttributeValue { S = fieldsJson },
                    [RegisteredAtAttribute] = new AttributeValue { S = DateTimeOffset.UtcNow.ToString("O") }
                }
            };

            await dynamoDb.PutItemAsync(request, token).ConfigureAwait(false);
            return nextVersion;
        }, ct).ConfigureAwait(false);
    }

    public async Task<bool> ValidateAsync(string tenantId, LogEntry entry, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<bool>();
        return await pipeline.ExecuteAsync(async token =>
        {
            var currentVersion = await GetCurrentVersionAsync(tenantId, token).ConfigureAwait(false);
            if (currentVersion == 0)
                return true;

            var request = new GetItemRequest
            {
                TableName = ResolveTableName(),
                Key = new Dictionary<string, AttributeValue>
                {
                    [PartitionKey] = new AttributeValue { S = tenantId },
                    [SortKey] = new AttributeValue { N = currentVersion.ToString(CultureInfo.InvariantCulture) }
                }
            };

            var response = await dynamoDb.GetItemAsync(request, token).ConfigureAwait(false);
            if (response.Item is null || response.Item.Count == 0)
                return true;

            if (!response.Item.TryGetValue(FieldsAttribute, out var fieldsAttr) || fieldsAttr.S is null)
                return true;

            var fields = JsonSerializer.Deserialize<List<SchemaField>>(fieldsAttr.S);
            if (fields is null)
                return true;

            var entryTags = entry.Tags ?? new Dictionary<string, string>();
            var requiredFields = fields.Where(f => !f.IsNullable).ToList();
            return requiredFields.All(f => entryTags.ContainsKey(f.Name));
        }, ct).ConfigureAwait(false);
    }

    public async Task<uint> InferAndRegisterAsync(string tenantId, LogEntry entry, CancellationToken ct = default)
    {
        var inferred = SchemaInferenceEngine.InferSchema([entry]);
        return await RegisterSchemaAsync(tenantId, inferred, ct).ConfigureAwait(false);
    }

    private string ResolveTableName()
    {
        if (!string.IsNullOrWhiteSpace(_opts.TableName))
            return _opts.TableName;

        return $"{_opts.TablePrefix}schema-registry";
    }
}
