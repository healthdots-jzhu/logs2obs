namespace Logs2Obs.Adapters.Aws.Tests.Search;

using Logs2Obs.Adapters.Aws.Search;

public sealed class OpenSearchIndexerTests
{
    private readonly Type _sutType = typeof(OpenSearchIndexer);

    [Fact(Skip = "Requires AWS OpenSearch domain for bulk indexing.")]
    public void IndexBatchAsync_WhenEntriesProvided_IndexesDocuments()
    {
        _ = _sutType;
    }

    [Fact(Skip = "Requires AWS OpenSearch domain for query execution.")]
    public void SearchAsync_WhenQueryProvided_ReturnsMatches()
    {
        _ = _sutType;
    }

    [Fact(Skip = "Requires AWS OpenSearch domain for aggregation queries.")]
    public void AggregateAsync_WhenAggRequested_ReturnsBuckets()
    {
        _ = _sutType;
    }

    [Fact(Skip = "Requires AWS OpenSearch domain for tenant deletes.")]
    public void DeleteByTenantAsync_WhenTenantProvided_RemovesDocuments()
    {
        _ = _sutType;
    }
}
