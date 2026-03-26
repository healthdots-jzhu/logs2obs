namespace Logs2Obs.Adapters.Aws.Tests.MetadataStore;

using System.Linq;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Logs2Obs.Adapters.Aws.MetadataStore;
using Logs2Obs.Adapters.Aws.Options;
using Options = Microsoft.Extensions.Options.Options;

public sealed class DynamoMetadataStoreTests
{
    private readonly Mock<IAmazonDynamoDB> _dynamo = new();
    private readonly string _tableName = "metadata";
    private readonly string _keyValue = "item-123";

    [Fact]
    public async Task GetAsync_WhenItemNotFound_ReturnsNull()
    {
        // Arrange
        var response = new GetItemResponse { Item = new Dictionary<string, AttributeValue>() };
        _dynamo.Setup(db => db.GetItemAsync(
                It.IsAny<GetItemRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        _dynamo.Setup(db => db.GetItemAsync(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, AttributeValue>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var sut = CreateSut();

        // Act
        var result = await sut.GetAsync<MetadataRecord>(_tableName, _keyValue, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_CallsDeleteItem()
    {
        // Arrange
        _dynamo.Setup(db => db.DeleteItemAsync(
                It.IsAny<DeleteItemRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteItemResponse());
        _dynamo.Setup(db => db.DeleteItemAsync(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, AttributeValue>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteItemResponse());
        var sut = CreateSut();

        // Act
        await sut.DeleteAsync(_tableName, _keyValue, CancellationToken.None);

        // Assert
        WasDeleteCalledFor(_keyValue).Should().BeTrue();
    }

    [Fact(Skip = "Requires AWS DynamoDB table schema and provisioning.")]
    public void PutAsync_WhenCalled_PersistsEntity()
    {
        _ = _tableName;
    }

    [Fact(Skip = "Requires AWS DynamoDB query and indexes.")]
    public void QueryAsync_WhenFilterMatches_ReturnsItems()
    {
        _ = _tableName;
    }

    private DynamoMetadataStore CreateSut()
    {
        var options = Options.Create(new AwsAdaptersOptions
        {
            Dynamo = new DynamoOptions()
        });
        return new DynamoMetadataStore(_dynamo.Object, options);
    }

    private bool WasDeleteCalledFor(string key) =>
        _dynamo.Invocations.Any(invocation =>
        {
            if (invocation.Method.Name != nameof(IAmazonDynamoDB.DeleteItemAsync))
                return false;

            if (invocation.Arguments.Count >= 2
                && invocation.Arguments[0] is string table
                && invocation.Arguments[1] is Dictionary<string, AttributeValue> keyMap)
            {
                return TableMatches(table) && RequestHasKey(keyMap, key);
            }

            if (invocation.Arguments.Count >= 1
                && invocation.Arguments[0] is DeleteItemRequest request)
            {
                return TableMatches(request.TableName) && RequestHasKey(request.Key, key);
            }

            return false;
        });

    private bool TableMatches(string table) =>
        table.Equals(_tableName, StringComparison.Ordinal)
        || table.EndsWith(_tableName, StringComparison.Ordinal);

    private static bool RequestHasKey(IReadOnlyDictionary<string, AttributeValue>? attributes, string keyValue) =>
        attributes?.Values.Any(value => value.S?.Contains(keyValue, StringComparison.Ordinal) == true) == true;

    private sealed record MetadataRecord(string Id);
}
