using FluentAssertions;
using Logs2Obs.Adapters.Local.MetadataStore;
using Logs2Obs.Adapters.Local.Options;
using Logs2Obs.Adapters.Local.Tests.Fixtures;

namespace Logs2Obs.Adapters.Local.Tests.MetadataStore;

using Options = Microsoft.Extensions.Options.Options;

/// <summary>Simple test entity stored and retrieved by key.</summary>
public sealed record TestEntity(string Key, string Value);

[Collection("PostgreSql")]
public class PostgresMetadataStoreTests(PostgreSqlFixture fixture)
{
    private PostgresMetadataStore CreateSut() =>
        new(Options.Create(new PostgresOptions { ConnectionString = fixture.ConnectionString }));

    [Fact]
    public async Task PutAsync_ThenGetAsync_ReturnsSameEntity()
    {
        // Arrange
        var sut = CreateSut();
        var entity = new TestEntity(Guid.NewGuid().ToString(), "value-one");

        // Act
        await sut.PutAsync("test_entities", entity);
        var result = await sut.GetAsync<TestEntity>("test_entities", entity.Key);

        // Assert
        result.Should().NotBeNull();
        result!.Key.Should().Be(entity.Key);
        result.Value.Should().Be(entity.Value);
    }

    [Fact]
    public async Task GetAsync_WhenKeyNotFound_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();
        var missingKey = Guid.NewGuid().ToString();

        // Act
        var result = await sut.GetAsync<TestEntity>("test_entities", missingKey);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_ThenGetAsync_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();
        var entity = new TestEntity(Guid.NewGuid().ToString(), "to-delete");
        await sut.PutAsync("test_entities", entity);

        // Act
        await sut.DeleteAsync("test_entities", entity.Key);
        var result = await sut.GetAsync<TestEntity>("test_entities", entity.Key);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task PutAsync_CalledTwice_Upserts()
    {
        // Arrange
        var sut = CreateSut();
        var key = Guid.NewGuid().ToString();
        var original = new TestEntity(key, "original");
        var updated  = new TestEntity(key, "updated");

        // Act
        await sut.PutAsync("test_entities", original);
        await sut.PutAsync("test_entities", updated);
        var result = await sut.GetAsync<TestEntity>("test_entities", key);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Should().Be("updated");
    }

    [Fact]
    public async Task QueryAsync_WithFilter_ReturnsMatchingEntities()
    {
        // Arrange
        var sut = CreateSut();
        var prefix = Guid.NewGuid().ToString();
        var a = new TestEntity(Guid.NewGuid().ToString(), $"{prefix}-match");
        var b = new TestEntity(Guid.NewGuid().ToString(), $"{prefix}-match");
        var c = new TestEntity(Guid.NewGuid().ToString(), "no-match");
        await sut.PutAsync("test_entities", a);
        await sut.PutAsync("test_entities", b);
        await sut.PutAsync("test_entities", c);

        // Act
        var results = new List<TestEntity>();
        await foreach (var entity in sut.QueryAsync<TestEntity>(
            "test_entities",
            e => e.Value.StartsWith(prefix)))
        {
            results.Add(entity);
        }

        // Assert
        results.Should().HaveCount(2);
        results.Should().AllSatisfy(e => e.Value.Should().StartWith(prefix));
    }
}
