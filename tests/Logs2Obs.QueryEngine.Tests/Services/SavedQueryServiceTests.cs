namespace Logs2Obs.QueryEngine.Tests.Services;

using Logs2Obs.QueryEngine.Services;
using Logs2Obs.QueryEngine.Tests.Helpers;

public class SavedQueryServiceTests
{
    [Fact]
    public async Task SaveAsync_PersistsToMetadataStore()
    {
        var store = new InMemoryMetadataStore();
        var service = new SavedQueryService(store, NullLogger<SavedQueryService>.Instance);
        var now = DateTimeOffset.UtcNow;
        var query = new SavedQuery
        {
            QueryId = "query-1",
            TenantId = "tenant-1",
            Name = "Errors last hour",
            Sql = "SELECT * FROM logs LIMIT 100",
            Description = "Test query",
            CreatedAt = now,
            UpdatedAt = now
        };

        await service.SaveAsync(query, CancellationToken.None);

        var key = $"savedquery:{query.TenantId}:{query.QueryId}";
        store.TryGet("saved_queries", key, out var record).Should().BeTrue();
        InMemoryMetadataStore.GetPropertyValue<string>(record!, "Key").Should().Be(key);
        InMemoryMetadataStore.GetPropertyValue<string>(record!, "Name").Should().Be(query.Name);
    }

    [Fact]
    public async Task GetAsync_WhenExists_ReturnsSavedQuery()
    {
        var store = new InMemoryMetadataStore();
        var service = new SavedQueryService(store, NullLogger<SavedQueryService>.Instance);
        var now = DateTimeOffset.UtcNow;
        var query = new SavedQuery
        {
            QueryId = "query-2",
            TenantId = "tenant-1",
            Name = "Requests",
            Sql = "SELECT * FROM logs LIMIT 50",
            Description = "List requests",
            CreatedAt = now,
            UpdatedAt = now
        };

        await service.SaveAsync(query, CancellationToken.None);

        var result = await service.GetAsync(query.QueryId, query.TenantId, CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(query);
    }

    [Fact]
    public async Task GetAsync_WhenNotFound_ReturnsNull()
    {
        var store = new InMemoryMetadataStore();
        var service = new SavedQueryService(store, NullLogger<SavedQueryService>.Instance);

        var result = await service.GetAsync("missing", "tenant-1", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_ReturnsTenantQueries()
    {
        var store = new InMemoryMetadataStore();
        var service = new SavedQueryService(store, NullLogger<SavedQueryService>.Instance);
        var now = DateTimeOffset.UtcNow;
        var tenantQuery = new SavedQuery
        {
            QueryId = "query-3",
            TenantId = "tenant-1",
            Name = "Tenant query",
            Sql = "SELECT * FROM logs LIMIT 10",
            CreatedAt = now,
            UpdatedAt = now
        };
        var tenantQueryTwo = new SavedQuery
        {
            QueryId = "query-4",
            TenantId = "tenant-1",
            Name = "Tenant query 2",
            Sql = "SELECT * FROM logs LIMIT 20",
            CreatedAt = now,
            UpdatedAt = now
        };
        var otherTenantQuery = new SavedQuery
        {
            QueryId = "query-5",
            TenantId = "tenant-2",
            Name = "Other tenant",
            Sql = "SELECT * FROM logs LIMIT 30",
            CreatedAt = now,
            UpdatedAt = now
        };

        await service.SaveAsync(tenantQuery, CancellationToken.None);
        await service.SaveAsync(tenantQueryTwo, CancellationToken.None);
        await service.SaveAsync(otherTenantQuery, CancellationToken.None);

        var results = new List<SavedQuery>();
        await foreach (var item in service.ListAsync("tenant-1", CancellationToken.None))
        {
            results.Add(item);
        }

        results.Should().HaveCount(2);
        results.Select(r => r.QueryId).Should().BeEquivalentTo(new[] { "query-3", "query-4" });
    }

    [Fact]
    public async Task DeleteAsync_RemovesFromMetadataStore()
    {
        var store = new InMemoryMetadataStore();
        var service = new SavedQueryService(store, NullLogger<SavedQueryService>.Instance);
        var now = DateTimeOffset.UtcNow;
        var query = new SavedQuery
        {
            QueryId = "query-6",
            TenantId = "tenant-1",
            Name = "Delete me",
            Sql = "SELECT * FROM logs LIMIT 5",
            CreatedAt = now,
            UpdatedAt = now
        };

        await service.SaveAsync(query, CancellationToken.None);

        await service.DeleteAsync(query.QueryId, query.TenantId, CancellationToken.None);

        var key = $"savedquery:{query.TenantId}:{query.QueryId}";
        store.TryGet("saved_queries", key, out _).Should().BeFalse();
    }
}
