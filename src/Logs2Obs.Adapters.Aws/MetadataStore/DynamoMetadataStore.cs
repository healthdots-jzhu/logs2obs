namespace Logs2Obs.Adapters.Aws.MetadataStore;

using System.Text.Json;
using System.Text.Json.Nodes;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Logs2Obs.Adapters.Aws.Options;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Resilience;
using Microsoft.Extensions.Options;

public sealed class DynamoMetadataStore(
    IAmazonDynamoDB dynamoDb,
    IOptions<AwsAdaptersOptions> options) : IMetadataStore
{
    private const string DataAttribute = "Data";
    private const string UpdatedAtAttribute = "UpdatedAt";
    private const string PartitionKey = "PK";
    private const string SortKey = "SK";
    private readonly DynamoOptions _opts = options.Value.Dynamo;
    private readonly bool _useSingleTable = !string.IsNullOrWhiteSpace(options.Value.Dynamo.TableName);

    public async Task<T?> GetAsync<T>(string table, string key, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<T?>();
        return await pipeline.ExecuteAsync(async token =>
        {
            var request = new GetItemRequest
            {
                TableName = ResolveTableName(table),
                Key = BuildKey(table, key)
            };

            var response = await dynamoDb.GetItemAsync(request, token).ConfigureAwait(false);
            if (response.Item is null || response.Item.Count == 0)
                return default;

            if (!response.Item.TryGetValue(DataAttribute, out var dataValue) || dataValue.S is null)
                return default;

            return JsonSerializer.Deserialize<T>(dataValue.S);
        }, ct).ConfigureAwait(false);
    }

    public async Task PutAsync<T>(string table, T entity, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<bool>();
        await pipeline.ExecuteAsync(async token =>
        {
            var key = ExtractKey(entity);
            var keyAttributes = BuildKey(table, key);
            var json = JsonSerializer.Serialize(entity);
            var request = new PutItemRequest
            {
                TableName = ResolveTableName(table),
                Item = new Dictionary<string, AttributeValue>
                {
                    [PartitionKey] = keyAttributes[PartitionKey],
                    [SortKey] = keyAttributes[SortKey],
                    [DataAttribute] = new AttributeValue { S = json },
                    [UpdatedAtAttribute] = new AttributeValue { S = DateTimeOffset.UtcNow.ToString("O") }
                }
            };

            await dynamoDb.PutItemAsync(request, token).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);

    }

    public async Task DeleteAsync(string table, string key, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<bool>();
        await pipeline.ExecuteAsync(async token =>
        {
            var request = new DeleteItemRequest
            {
                TableName = ResolveTableName(table),
                Key = BuildKey(table, key)
            };
            await dynamoDb.DeleteItemAsync(request, token).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<T> QueryAsync<T>(
        string table,
        Func<T, bool> filter,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<ScanResponse>();
        Dictionary<string, AttributeValue>? lastKey = null;
        do
        {
            var request = new ScanRequest
            {
                TableName = ResolveTableName(table),
                ExclusiveStartKey = lastKey
            };

            var response = await pipeline.ExecuteAsync(
                async token => await dynamoDb.ScanAsync(request, token).ConfigureAwait(false), ct)
                .ConfigureAwait(false);

            foreach (var item in response.Items)
            {
                ct.ThrowIfCancellationRequested();

                if (_useSingleTable && !MatchesTable(item, table))
                    continue;

                if (!item.TryGetValue(DataAttribute, out var dataValue) || dataValue.S is null)
                    continue;

                var entity = JsonSerializer.Deserialize<T>(dataValue.S);
                if (entity is not null && filter(entity))
                    yield return entity;
            }

            lastKey = response.LastEvaluatedKey;
        } while (lastKey is { Count: > 0 });
    }

    private static bool MatchesTable(Dictionary<string, AttributeValue> item, string table)
    {
        if (!item.TryGetValue(PartitionKey, out var pk) || pk.S is null)
            return false;

        return pk.S.StartsWith($"{table}#", StringComparison.Ordinal);
    }

    private Dictionary<string, AttributeValue> BuildKey(string table, string key)
    {
        var pkValue = _useSingleTable ? $"{table}#{key}" : key;
        return new Dictionary<string, AttributeValue>
        {
            [PartitionKey] = new AttributeValue { S = pkValue },
            [SortKey] = new AttributeValue { S = "metadata" }
        };
    }

    private string ResolveTableName(string table)
    {
        if (!string.IsNullOrWhiteSpace(_opts.TableName))
            return _opts.TableName;

        return $"{_opts.TablePrefix}{table}";
    }

    private static string ExtractKey<T>(T entity)
    {
        var node = JsonSerializer.SerializeToNode(entity)?.AsObject();
        if (node is null)
            throw new InvalidOperationException("Cannot serialize entity to extract key.");

        foreach (var candidate in new[] { "id", "key", "tenantId", "queryId", "jobId", "ruleId", "executionId" })
        {
            if (node.TryGetPropertyValue(candidate, out var val) && val is not null)
                return val.GetValue<string>();
        }

        foreach (var prop in node)
        {
            if (prop.Key.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && prop.Value is not null)
                return prop.Value.GetValue<string>();
        }

        throw new InvalidOperationException(
            $"Cannot derive key from {typeof(T).Name}. Add a property named 'Id', 'Key', or '*Id'.");
    }
}
